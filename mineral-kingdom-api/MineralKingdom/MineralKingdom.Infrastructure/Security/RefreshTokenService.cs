using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class RefreshTokenService
{
  private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);
  private const int RawTokenBytes = 32; // 256-bit

  private readonly MineralKingdomDbContext _db;

  public RefreshTokenService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  /// <summary>
  /// Creates a new refresh token for the user and stores only the hash in the DB.
  /// Returns the DB row + the raw token (raw token must be returned to client once).
  /// </summary>
  public async Task<(RefreshToken tokenRow, string rawToken)> CreateAsync(
    Guid userId,
    DateTime utcNow,
    TimeSpan? lifetime,
    CancellationToken ct)
  {
    var raw = GenerateRawToken();
    var hash = ComputeTokenHash(raw);

    var token = new RefreshToken
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      TokenHash = hash,
      CreatedAt = utcNow,
      ExpiresAt = utcNow.Add(lifetime ?? DefaultLifetime),
      UsedAt = null,
      RevokedAt = null,
      ReplacedByTokenHash = null
    };

    _db.RefreshTokens.Add(token);
    await _db.SaveChangesAsync(ct);

    return (token, raw);
  }

  /// <summary>
  /// Rotates a refresh token. On success, marks the old token as Used and creates a new token.
  /// If the provided token was already used/revoked, this is treated as token reuse.
  /// </summary>
  public async Task<RotateResult> RotateAsync(string rawRefreshToken, DateTime utcNow, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(rawRefreshToken))
    {
      return RotateResult.Invalid("INVALID_INPUT");
    }

    var hash = ComputeTokenHash(rawRefreshToken);

    var existing = await _db.RefreshTokens
      .AsTracking()
      .FirstOrDefaultAsync(x => x.TokenHash == hash, ct);

    if (existing is null)
    {
      return RotateResult.Invalid("INVALID_OR_EXPIRED_REFRESH_TOKEN");
    }

    if (existing.ExpiresAt <= utcNow)
    {
      return RotateResult.Invalid("INVALID_OR_EXPIRED_REFRESH_TOKEN");
    }

    // Reuse detection: used or revoked token presented again
    if (existing.UsedAt is not null || existing.RevokedAt is not null)
    {
      await RevokeAllForUserAsync(existing.UserId, utcNow, ct);
      return RotateResult.Reused();
    }

    // Normal rotation
    var (newRow, newRaw) = await CreateAsync(existing.UserId, utcNow, DefaultLifetime, ct);

    existing.UsedAt = utcNow;
    existing.ReplacedByTokenHash = newRow.TokenHash;

    await _db.SaveChangesAsync(ct);

    return RotateResult.Success(existing.UserId, newRaw);
  }

  /// <summary>
  /// Revokes all refresh tokens for a user (used for reuse detection + future logout).
  /// </summary>
  public async Task RevokeAllForUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
  {
    var tokens = await _db.RefreshTokens
      .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > utcNow)
      .ToListAsync(ct);

    foreach (var t in tokens)
    {
      t.RevokedAt = utcNow;
    }

    await _db.SaveChangesAsync(ct);
  }

  // -----------------------
  // Token helpers (private)
  // -----------------------

  private static string GenerateRawToken()
  {
    var bytes = RandomNumberGenerator.GetBytes(RawTokenBytes);
    // URL-safe Base64 (no padding)
    return Convert.ToBase64String(bytes)
      .Replace("+", "-")
      .Replace("/", "_")
      .Replace("=", "");
  }

  public static string ComputeTokenHash(string rawToken)
  {
    // SHA-256 over UTF8 string, store as hex
    var bytes = Encoding.UTF8.GetBytes(rawToken);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash); // uppercase hex
  }

  // -----------------------
  // Result type
  // -----------------------

  public sealed record RotateResult(
    bool Ok,
    Guid? UserId,
    string? NewRefreshToken,
    string ErrorCode)
  {
    public static RotateResult Success(Guid userId, string newRaw)
      => new(true, userId, newRaw, string.Empty);

    public static RotateResult Invalid(string code)
      => new(false, null, null, code);

    public static RotateResult Reused()
      => new(false, null, null, "REFRESH_TOKEN_REUSED");
  }
}
