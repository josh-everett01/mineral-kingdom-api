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

  public AuthService(
      MineralKingdomDbContext db,
      PasswordHasher<User> passwordHasher,
      EmailVerificationTokenService tokenService,
      IMKEmailSender emailSender)
  {
    _db = db;
    _passwordHasher = passwordHasher;
    _tokenService = tokenService;
    _emailSender = emailSender;
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
    var user = await _db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);

    if (user is null || user.EmailVerified)
    {
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
