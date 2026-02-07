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

  // Payment-confirmed snapshot
  public string Status { get; set; } = "DRAFT"; // DRAFT or PAID
  public DateTimeOffset? PaidAt { get; set; }
  public int SubtotalCents { get; set; }
  public int DiscountTotalCents { get; set; }
  public int TotalCents { get; set; }

  public string CurrencyCode { get; set; } = "USD";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<OrderLine> Lines { get; set; } = new();
}
