using System;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class BackgroundJob
{
  public Guid Id { get; set; }

  public string Type { get; set; } = default!;
  public string Status { get; set; } = default!;

  public string? PayloadJson { get; set; }

  public int Attempts { get; set; }
  public int MaxAttempts { get; set; }

  public DateTimeOffset RunAt { get; set; }

  public DateTimeOffset? LockedAt { get; set; }
  public string? LockedBy { get; set; }

  public string? LastError { get; set; }

  public DateTimeOffset? CompletedAt { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
