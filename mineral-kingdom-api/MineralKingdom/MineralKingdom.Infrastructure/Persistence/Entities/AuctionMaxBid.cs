namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class AuctionMaxBid
{
  public Guid AuctionId { get; set; }
  public Auction? Auction { get; set; }

  public Guid UserId { get; set; }

  public int MaxBidCents { get; set; }

  // "IMMEDIATE" | "DELAYED" (future story uses this)
  public string BidType { get; set; } = "IMMEDIATE";

  // Server authoritative
  public DateTimeOffset ReceivedAt { get; set; }
}
