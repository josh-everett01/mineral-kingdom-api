using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class EmailVerificationToken
{
  public Guid Id { get; set; }

  public Guid UserId { get; set; }

  public User User { get; set; } = null!;

  [MaxLength(128)]
  public string TokenHash { get; set; } = string.Empty;

  public DateTime ExpiresAt { get; set; }

  public DateTime? UsedAt { get; set; }

  public DateTime CreatedAt { get; set; }
}
