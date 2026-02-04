namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Order
{
  public Guid Id { get; set; }

  public Guid UserId { get; set; }

  // Snapshot totals (Definition of Done)
  public int SubtotalCents { get; set; }
  public int DiscountTotalCents { get; set; }
  public int TotalCents { get; set; }

  // Optional but useful for future
  public string CurrencyCode { get; set; } = "USD";
  public string Status { get; set; } = "DRAFT";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<OrderLine> Lines { get; set; } = new();
}
