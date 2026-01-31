using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Security;

public sealed class AuthService
{
  private readonly MineralKingdomDbContext _db;
  private readonly PasswordHasher<User> _passwordHasher;
  private readonly EmailVerificationTokenService _tokenService;
  private readonly IMKEmailSender _emailSender;
  private readonly RefreshTokenService _refreshTokens;
  private readonly JwtTokenService _jwt;

  public AuthService(
      MineralKingdomDbContext db,
      PasswordHasher<User> passwordHasher,
      EmailVerificationTokenService tokenService,
      IMKEmailSender emailSender,
      RefreshTokenService refreshTokens,
      JwtTokenService jwt)
  {
    _db = db;
    _passwordHasher = passwordHasher;
    _tokenService = tokenService;
    _emailSender = emailSender;
    _refreshTokens = refreshTokens;
    _jwt = jwt;
  }

  public async Task<(User user, string rawVerificationToken)> RegisterAsync(
    string email,
    string password,
    string verificationBaseUrl,
    DateTime utcNow,
    CancellationToken ct)
  {
    var normalizedEmail = email.Trim().ToLowerInvariant();

    var exists = await _db.Users.AnyAsync(x => x.Email == normalizedEmail, ct);
    if (exists)
      throw new InvalidOperationException("EMAIL_ALREADY_IN_USE");

    ValidatePasswordOrThrow(password);

    var user = new User
    {
      Id = Guid.NewGuid(),
      Email = normalizedEmail,
      EmailVerified = false,
      CreatedAt = utcNow,
      UpdatedAt = utcNow,
    };

    user.PasswordHash = _passwordHasher.HashPassword(user, password);

    _db.Users.Add(user);
    await _db.SaveChangesAsync(ct);

    // Create token (service should revoke prior active tokens for this user)
    var (_, raw) = await _tokenService.CreateAsync(user.Id, utcNow, null, ct);

    // Frontend link strategy (Option A):
    // verificationBaseUrl should be something like: https://your-frontend.com/verify-email
    var link = BuildVerificationLink(verificationBaseUrl, raw);

    await _emailSender.SendEmailVerificationAsync(user.Email, link, ct);

    return (user, raw);
  }


  public async Task<bool> VerifyEmailAsync(string rawToken, DateTime utcNow, CancellationToken ct)
  {
    var token = await _tokenService.FindValidAsync(rawToken, utcNow, ct);
    if (token is null)
    {
      return false;
    }

    token.User.EmailVerified = true;
    token.User.UpdatedAt = utcNow;
    await _tokenService.MarkUsedAsync(token, utcNow, ct);
    return true;
  }

  public async Task<(bool sent, string? rawToken)> ResendVerificationAsync(
  string email,
  string verificationBaseUrl,
  DateTime utcNow,
  CancellationToken ct)
  {
    var normalizedEmail = email.Trim().ToLowerInvariant();

    // Using FirstOrDefault avoids a crash if duplicates ever exist due to bad seed/test data.
    var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);

    if (user is null || user.EmailVerified)
    {
      // Always return "sent" to prevent email enumeration
      return (sent: true, rawToken: null);
    }

    var (_, raw) = await _tokenService.CreateAsync(user.Id, utcNow, null, ct);

    var link = BuildVerificationLink(verificationBaseUrl, raw);
    await _emailSender.SendEmailVerificationAsync(user.Email, link, ct);

    return (sent: true, rawToken: raw);
  }


  private static string BuildVerificationLink(string verificationBaseUrl, string rawToken)
  {
    var encoded = Uri.EscapeDataString(rawToken);
    return $"{verificationBaseUrl}?token={encoded}";
  }

  public sealed record AuthTokensResponse(
  string AccessToken,
  int ExpiresIn,
  string RefreshToken);

  public sealed record RefreshResult(
    bool Ok,
    AuthTokensResponse? Tokens,
    string? ErrorCode)
  {
    public static RefreshResult Success(AuthTokensResponse tokens) => new(true, tokens, null);
    public static RefreshResult Fail(string code) => new(false, null, code);
  }

  public async Task<AuthTokensResponse> LoginAsync(
    string email,
    string password,
    DateTime utcNow,
    CancellationToken ct)
  {
    var normalizedEmail = email.Trim().ToLowerInvariant();

    var user = await _db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
    if (user is null)
      throw new InvalidOperationException("INVALID_CREDENTIALS");

    if (!user.EmailVerified)
      throw new InvalidOperationException("EMAIL_NOT_VERIFIED");

    var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
    if (verify == PasswordVerificationResult.Failed)
      throw new InvalidOperationException("INVALID_CREDENTIALS");

    // Issue JWT + refresh
    var (accessToken, expiresIn) = _jwt.CreateAccessToken(user, utcNow);
    var (_, rawRefresh) = await _refreshTokens.CreateAsync(user.Id, utcNow, null, ct);

    return new AuthTokensResponse(accessToken, expiresIn, rawRefresh);
  }

  public async Task<RefreshResult> RefreshAsync(string rawRefreshToken, DateTime utcNow, CancellationToken ct)
  {
    var rotate = await _refreshTokens.RotateAsync(rawRefreshToken, utcNow, ct);

    if (!rotate.Ok)
    {
      // rotate.ErrorCode can be: INVALID_INPUT, INVALID_OR_EXPIRED_REFRESH_TOKEN, REFRESH_TOKEN_REUSED
      return RefreshResult.Fail(rotate.ErrorCode);
    }

    // Load user to mint JWT (we also ensure email verified still true)
    var userId = rotate.UserId!.Value;

    var user = await _db.Users.SingleAsync(x => x.Id == userId, ct);

    if (!user.EmailVerified)
    {
      // If someone loses verification (edge case) revoke tokens
      await _refreshTokens.RevokeAllForUserAsync(userId, utcNow, ct);
      return RefreshResult.Fail("EMAIL_NOT_VERIFIED");
    }

    var (accessToken, expiresIn) = _jwt.CreateAccessToken(user, utcNow);

    return RefreshResult.Success(new AuthTokensResponse(
      AccessToken: accessToken,
      ExpiresIn: expiresIn,
      RefreshToken: rotate.NewRefreshToken!));
  }

  private static void ValidatePasswordOrThrow(string password)
  {
    if (password.Length < 10)
      throw new ValidationException("Password must be at least 10 characters.");

    var hasUpper = password.Any(char.IsUpper);
    var hasLower = password.Any(char.IsLower);
    var hasDigit = password.Any(char.IsDigit);

    if (!hasUpper || !hasLower || !hasDigit)
      throw new ValidationException("Password must include upper, lower, and a number.");
  }
}
