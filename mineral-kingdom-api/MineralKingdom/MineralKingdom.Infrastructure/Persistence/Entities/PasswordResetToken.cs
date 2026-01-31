namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class PasswordResetToken
{
  public Guid Id { get; set; }

  public Guid UserId { get; set; }
  public User User { get; set; } = default!;

  public string TokenHash { get; set; } = default!; // store hash only

  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
  public DateTime? UsedAt { get; set; }
}
