namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class AuctionDelayedBid
{
  public Guid AuctionId { get; set; }
  public Auction? Auction { get; set; }

  public Guid UserId { get; set; }

  public int MaxBidCents { get; set; }

  // SCHEDULED | MOOT | ACTIVATED | CANCELLED
  public string Status { get; set; } = "SCHEDULED";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public DateTimeOffset? CancelledAt { get; set; }
  public DateTimeOffset? MootedAt { get; set; }
  public DateTimeOffset? ActivatedAt { get; set; }
}