using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class PasswordResetService
{
  private readonly MineralKingdomDbContext _db;
  private readonly PasswordResetTokenService _tokens;
  private readonly PasswordHasher<User> _hasher;
  private readonly IMKEmailSender _email;
  private readonly RefreshTokenService _refreshTokens;

  public PasswordResetService(
    MineralKingdomDbContext db,
    PasswordResetTokenService tokens,
    PasswordHasher<User> hasher,
    IMKEmailSender email,
    RefreshTokenService refreshTokens)
  {
    _db = db;
    _tokens = tokens;
    _hasher = hasher;
    _email = email;
    _refreshTokens = refreshTokens;
  }

  public async Task<(bool sent, string? rawToken)> RequestAsync(
    string email,
    string resetBaseUrl,
    DateTime utcNow,
    CancellationToken ct)
  {
    var normalizedEmail = email.Trim().ToLowerInvariant();

    var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);

    // ✅ Prevent email enumeration: always say "sent"
    if (user is null)
      return (true, null);

    // Create token + send link
    var (_, raw) = await _tokens.CreateAsync(user.Id, utcNow, null, ct);

    var link = BuildResetLink(resetBaseUrl, raw);
    await _email.SendPasswordResetAsync(user.Email, link, ct);

    return (true, raw);
  }

  public async Task<(bool ok, string? error)> ConfirmAsync(
    string rawToken,
    string newPassword,
    DateTime utcNow,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(newPassword))
      return (false, "INVALID_INPUT");

    ValidatePasswordOrThrow(newPassword);

    var token = await _tokens.FindValidAsync(rawToken, utcNow, ct);
    if (token is null)
      return (false, "INVALID_OR_EXPIRED_TOKEN");

    var user = token.User;

    user.PasswordHash = _hasher.HashPassword(user, newPassword);
    user.UpdatedAt = utcNow;

    await _tokens.MarkUsedAsync(token, utcNow, ct);

    // ✅ Recommended: revoke all refresh tokens after password reset
    await _refreshTokens.RevokeAllForUserAsync(user.Id, utcNow, ct);

    return (true, null);
  }

  private static string BuildResetLink(string baseUrl, string rawToken)
  {
    var encoded = Uri.EscapeDataString(rawToken);
    return $"{baseUrl}?token={encoded}";
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
