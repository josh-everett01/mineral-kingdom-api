using System;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class RefreshToken
{
  public Guid Id { get; set; }

  public Guid UserId { get; set; }
  public User User { get; set; } = null!;

  // Store ONLY the hash (never store raw token)
  public string TokenHash { get; set; } = null!;

  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }

  // Rotation / security
  public DateTime? UsedAt { get; set; }
  public DateTime? RevokedAt { get; set; }

  // Audit chain
  public string? ReplacedByTokenHash { get; set; }
}
