namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class AuctionBidEvent
{
  public Guid Id { get; set; }

  public Guid AuctionId { get; set; }
  public Auction? Auction { get; set; }

  public Guid? UserId { get; set; } // null for system events

  public string EventType { get; set; } = default!; // AUCTION_CREATED, STATUS_CHANGED, ...

  public int? SubmittedAmountCents { get; set; }
  public bool? Accepted { get; set; }

  public int? ResultingCurrentPriceCents { get; set; }
  public Guid? ResultingLeaderUserId { get; set; }

  public string? DataJson { get; set; } // optional details (from/to status, etc)

  public DateTimeOffset ServerReceivedAt { get; set; }
}
