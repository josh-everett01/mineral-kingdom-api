namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Auction
{
  public Guid Id { get; set; }

  public Guid ListingId { get; set; }

  // e.g. DRAFT / SCHEDULED / LIVE / CLOSING / CLOSED
  public string Status { get; set; } = string.Empty;

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
