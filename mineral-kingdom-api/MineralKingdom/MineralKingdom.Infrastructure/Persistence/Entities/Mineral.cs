using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Mineral
{
  public Guid Id { get; set; }

  [MaxLength(200)]
  public string Name { get; set; } = string.Empty;

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
