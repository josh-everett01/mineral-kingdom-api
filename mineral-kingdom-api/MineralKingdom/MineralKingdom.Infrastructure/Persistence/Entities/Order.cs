namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Order
{
  public Guid Id { get; set; }

  // Member orders have UserId; guest orders have GuestEmail
  public Guid? UserId { get; set; }
  public string? GuestEmail { get; set; }

  // Human-friendly order identifier (guest lookup uses this + email)
  public string OrderNumber { get; set; } = default!;

  // Idempotency: one paid order per hold
  public Guid? CheckoutHoldId { get; set; }

  // ===== S5-4: Auction order metadata =====
  // STORE (default) or AUCTION
  public string SourceType { get; set; } = "STORE";

  // When SourceType=AUCTION, this links the order to a closed auction
  public Guid? AuctionId { get; set; }

  // For AUCTION orders (and future), indicates payment deadline
  public DateTimeOffset? PaymentDueAt { get; set; }
  // =======================================

  // Payment-confirmed snapshot
  public string Status { get; set; } = "DRAFT"; // DRAFT, AWAITING_PAYMENT, PAID
  public DateTimeOffset? PaidAt { get; set; }
  public int SubtotalCents { get; set; }
  public int DiscountTotalCents { get; set; }
  public int TotalCents { get; set; }

  public string CurrencyCode { get; set; } = "USD";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<OrderLine> Lines { get; set; } = new();

  public Guid? FulfillmentGroupId { get; set; }
  public FulfillmentGroup? FulfillmentGroup { get; set; }
  public List<OrderRefund> Refunds { get; set; } = new();
}
