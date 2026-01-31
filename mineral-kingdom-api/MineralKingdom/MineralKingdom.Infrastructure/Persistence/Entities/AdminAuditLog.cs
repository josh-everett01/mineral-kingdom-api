using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class AdminAuditLog
{
  public Guid Id { get; set; }

  public Guid ActorUserId { get; set; }
  public Guid TargetUserId { get; set; }

  [MaxLength(100)]
  public string Action { get; set; } = "ROLE_CHANGED";

  [MaxLength(20)]
  public string BeforeRole { get; set; } = string.Empty;

  [MaxLength(20)]
  public string AfterRole { get; set; } = string.Empty;

  public DateTime CreatedAt { get; set; }
}
