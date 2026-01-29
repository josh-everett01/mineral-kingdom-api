using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class User
{
  public Guid Id { get; set; }

  [MaxLength(320)]
  public string Email { get; set; } = string.Empty;

  [MaxLength(500)]
  public string PasswordHash { get; set; } = string.Empty;

  public bool EmailVerified { get; set; }

  public DateTime CreatedAt { get; set; }

  public DateTime UpdatedAt { get; set; }
}
