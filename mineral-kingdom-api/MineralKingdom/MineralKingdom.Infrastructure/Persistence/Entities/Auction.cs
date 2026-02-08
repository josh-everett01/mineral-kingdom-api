namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Auction
{
  public Guid Id { get; set; }

  public Guid ListingId { get; set; }

  // DRAFT / LIVE / CLOSING / CLOSED_WAITING_ON_PAYMENT / CLOSED_PAID / CLOSED_NOT_SOLD
  public string Status { get; set; } = "DRAFT";

  // Pricing / rules
  public int StartingPriceCents { get; set; }
  public int? ReservePriceCents { get; set; }

  // Timing (server authoritative)
  public DateTimeOffset? StartTime { get; set; } // optional in S5-1
  public DateTimeOffset CloseTime { get; set; }

  // Set when entering CLOSING
  public DateTimeOffset? ClosingWindowEnd { get; set; }

  // Derived fields (server-owned)
  public int CurrentPriceCents { get; set; }
  public Guid? CurrentLeaderUserId { get; set; }
  public int? CurrentLeaderMaxCents { get; set; } // hidden / internal
  public int BidCount { get; set; }
  public bool ReserveMet { get; set; }

  // Future-proofing
  public Guid? RelistOfAuctionId { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
