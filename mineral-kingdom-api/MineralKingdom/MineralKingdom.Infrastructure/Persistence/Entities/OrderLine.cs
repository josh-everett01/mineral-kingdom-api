namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class OrderLine
{
  public Guid Id { get; set; }

  public Guid OrderId { get; set; }
  public Order? Order { get; set; }

  // What was purchased (offer/listing)
  public Guid OfferId { get; set; }
  public Guid ListingId { get; set; }

  // Snapshot of pricing at time of order
  public int UnitPriceCents { get; set; }
  public int UnitDiscountCents { get; set; }
  public int UnitFinalPriceCents { get; set; }

  public int Quantity { get; set; } = 1;

  public int LineSubtotalCents { get; set; }
  public int LineDiscountCents { get; set; }
  public int LineTotalCents { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
