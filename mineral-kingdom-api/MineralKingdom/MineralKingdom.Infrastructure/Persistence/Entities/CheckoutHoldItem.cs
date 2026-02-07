using System;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CheckoutHoldItem
{
  public Guid Id { get; set; }

  public Guid HoldId { get; set; }
  public CheckoutHold? Hold { get; set; }

  public Guid ListingId { get; set; }
  public Listing? Listing { get; set; }

  public Guid OfferId { get; set; }
  public StoreOffer? Offer { get; set; }

  /// <summary>
  /// True only while the parent hold is ACTIVE. This enables a partial unique index:
  /// UNIQUE(ListingId) WHERE IsActive = true
  /// </summary>
  public bool IsActive { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}
