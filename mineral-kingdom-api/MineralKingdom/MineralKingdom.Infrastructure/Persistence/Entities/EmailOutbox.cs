using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class EmailOutbox
{
  public Guid Id { get; set; }

  [MaxLength(320)]
  public string ToEmail { get; set; } = null!;

  [MaxLength(80)]
  public string TemplateKey { get; set; } = null!;

  // Stored as jsonb
  public string PayloadJson { get; set; } = "{}";

  // Unique dedupe key (DoD)
  [MaxLength(200)]
  public string DedupeKey { get; set; } = null!;

  [MaxLength(20)]
  public string Status { get; set; } = "PENDING"; // PENDING | SENT | FAILED | DEAD_LETTER

  public int Attempts { get; set; } = 0;
  public int MaxAttempts { get; set; } = 8;

  public string? LastError { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? SentAt { get; set; }
}