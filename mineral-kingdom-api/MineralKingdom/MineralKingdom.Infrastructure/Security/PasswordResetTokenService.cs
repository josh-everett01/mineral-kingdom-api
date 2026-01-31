using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class PasswordResetTokenService
{
  private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);
  private readonly MineralKingdomDbContext _db;

  public PasswordResetTokenService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public static string ComputeTokenHash(string rawToken)
  {
    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(rawToken);
    var hash = sha.ComputeHash(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  public static string GenerateRawToken(int bytes = 32)
  {
    var buffer = RandomNumberGenerator.GetBytes(bytes);
    return Convert.ToBase64String(buffer)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }

  public async Task<(PasswordResetToken tokenRow, string rawToken)> CreateAsync(
    Guid userId,
    DateTime utcNow,
    TimeSpan? lifetime,
    CancellationToken ct)
  {
    // Revoke prior active reset tokens for this user (single active token)
    var active = await _db.PasswordResetTokens
      .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > utcNow)
      .ToListAsync(ct);

    foreach (var t in active)
      t.UsedAt = utcNow;

    if (active.Count > 0)
      await _db.SaveChangesAsync(ct);

    var raw = GenerateRawToken();
    var hash = ComputeTokenHash(raw);

    var row = new PasswordResetToken
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      TokenHash = hash,
      CreatedAt = utcNow,
      ExpiresAt = utcNow.Add(lifetime ?? DefaultLifetime),
      UsedAt = null
    };

    _db.PasswordResetTokens.Add(row);
    await _db.SaveChangesAsync(ct);

    return (row, raw);
  }

  public async Task<PasswordResetToken?> FindValidAsync(string rawToken, DateTime utcNow, CancellationToken ct)
  {
    var hash = ComputeTokenHash(rawToken);

    return await _db.PasswordResetTokens
      .Include(x => x.User)
      .SingleOrDefaultAsync(x => x.TokenHash == hash && x.UsedAt == null && x.ExpiresAt > utcNow, ct);
  }

  public async Task MarkUsedAsync(PasswordResetToken token, DateTime utcNow, CancellationToken ct)
  {
    token.UsedAt = utcNow;
    await _db.SaveChangesAsync(ct);
  }
}
