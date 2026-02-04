

using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class StoreOffer
{
  public Guid Id { get; set; }

  // Offer targets a listing (fixed-price store path)
  public Guid ListingId { get; set; }

  // Money is stored in cents (AC requirement)
  public int PriceCents { get; set; }

  // Flat or percentage discount supported
  public string DiscountType { get; set; } = DiscountTypes.None;

  // Only one of these should be set depending on DiscountType
  public int? DiscountCents { get; set; }           // for FLAT
  public int? DiscountPercentBps { get; set; }      // for PERCENT (basis points; 10000 = 100%)

  // Lifecycle / availability
  public bool IsActive { get; set; } = true;
  public DateTimeOffset? StartsAt { get; set; }
  public DateTimeOffset? EndsAt { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? DeletedAt { get; set; }
}
