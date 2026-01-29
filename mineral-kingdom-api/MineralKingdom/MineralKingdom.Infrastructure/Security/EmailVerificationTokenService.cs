using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class EmailVerificationTokenService
{
  private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);
  private readonly MineralKingdomDbContext _db;

  public EmailVerificationTokenService(MineralKingdomDbContext db)
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

  public async Task<(EmailVerificationToken tokenRow, string rawToken)> CreateAsync(Guid userId, DateTime utcNow, TimeSpan? lifetime, CancellationToken ct)
  {

    var now = DateTime.UtcNow;

    var activeTokens = await _db.EmailVerificationTokens
        .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now)
        .ToListAsync(ct);

    foreach (var t in activeTokens)
    {
      t.UsedAt = now; // revoke old tokens
    }

    await _db.SaveChangesAsync(ct);

    var raw = GenerateRawToken();
    var hash = ComputeTokenHash(raw);

    var token = new EmailVerificationToken
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      TokenHash = hash,
      CreatedAt = utcNow,
      ExpiresAt = utcNow.Add(lifetime ?? DefaultLifetime),
    };

    _db.EmailVerificationTokens.Add(token);
    await _db.SaveChangesAsync(ct);

    return (token, raw);
  }

  public async Task<EmailVerificationToken?> FindValidAsync(string rawToken, DateTime utcNow, CancellationToken ct)
  {
    var hash = ComputeTokenHash(rawToken);
    return await _db.EmailVerificationTokens
        .Include(x => x.User)
        .SingleOrDefaultAsync(x => x.TokenHash == hash && x.UsedAt == null && x.ExpiresAt > utcNow, ct);
  }

  public async Task MarkUsedAsync(EmailVerificationToken token, DateTime utcNow, CancellationToken ct)
  {
    token.UsedAt = utcNow;
    await _db.SaveChangesAsync(ct);
  }
}
