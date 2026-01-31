using System;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class AdminAuditLog
{
  public Guid Id { get; set; }

  public Guid ActorUserId { get; set; }
  public string? ActorRole { get; set; }

  public string ActionType { get; set; } = default!;

  public string EntityType { get; set; } = default!;
  public Guid EntityId { get; set; }

  public string? BeforeJson { get; set; }
  public string? AfterJson { get; set; }

  public string? IpAddress { get; set; }
  public string? UserAgent { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}
