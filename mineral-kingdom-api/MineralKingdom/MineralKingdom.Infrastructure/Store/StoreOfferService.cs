using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Store;

public sealed class StoreOfferService
{
  private readonly MineralKingdomDbContext _db;

  public StoreOfferService(MineralKingdomDbContext db) => _db = db;

  public async Task<(bool Ok, string? Error, StoreOffer? Offer)> GetAsync(Guid offerId, CancellationToken ct)
  {
    var offer = await _db.StoreOffers.SingleOrDefaultAsync(x => x.Id == offerId, ct);
    if (offer is null || !IsOfferCurrentlyValid(offer, DateTimeOffset.UtcNow))
      return (false, "OFFER_NOT_FOUND", null);

    return (true, null, offer);
  }

  public async Task<(bool Ok, string? Error, StoreOffer? Offer)> GetForListingAsync(Guid listingId, CancellationToken ct)
  {
    var offer = await _db.StoreOffers.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
    if (offer is null || !IsOfferCurrentlyValid(offer, DateTimeOffset.UtcNow))
      return (false, "OFFER_NOT_FOUND", null);

    return (true, null, offer);
  }

  public async Task<(bool Ok, string? Error, StoreOffer? Offer)> UpsertAsync(
    Guid listingId,
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps,
    bool isActive,
    DateTimeOffset? startsAt,
    DateTimeOffset? endsAt,
    CancellationToken ct)
  {
    if (priceCents <= 0) return (false, "INVALID_PRICE", null);

    discountType = (discountType ?? "").Trim().ToUpperInvariant();
    if (!DiscountTypes.IsValid(discountType)) return (false, "INVALID_DISCOUNT_TYPE", null);

    if (startsAt.HasValue && endsAt.HasValue && endsAt.Value < startsAt.Value)
      return (false, "INVALID_DATE_RANGE", null);

    // Normalize discount fields based on type
    if (discountType == DiscountTypes.None)
    {
      discountCents = null;
      discountPercentBps = null;
    }
    else if (discountType == DiscountTypes.Flat)
    {
      if (discountCents is null || discountCents <= 0) return (false, "INVALID_DISCOUNT_CENTS", null);
      discountPercentBps = null;
    }
    else if (discountType == DiscountTypes.Percent)
    {
      if (discountPercentBps is null || discountPercentBps <= 0 || discountPercentBps > 10000)
        return (false, "INVALID_DISCOUNT_PERCENT_BPS", null);
      discountCents = null;
    }

    // NOTE: Listing existence check (optional but helpful)
    var listingExists = await _db.Listings.AsNoTracking().AnyAsync(x => x.Id == listingId, ct);
    if (!listingExists) return (false, "LISTING_NOT_FOUND", null);

    var now = DateTimeOffset.UtcNow;

    var existing = await _db.StoreOffers
      .SingleOrDefaultAsync(x => x.ListingId == listingId && x.DeletedAt == null, ct);

    if (existing is null)
    {
      existing = new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        CreatedAt = now,
        UpdatedAt = now,
        IsActive = true
      };

      _db.StoreOffers.Add(existing);
    }

    existing.PriceCents = priceCents;
    existing.DiscountType = discountType;
    existing.DiscountCents = discountCents;
    existing.DiscountPercentBps = discountPercentBps;
    existing.IsActive = isActive;
    existing.StartsAt = startsAt;
    existing.EndsAt = endsAt;
    existing.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null, existing);
  }

  public static bool IsOfferCurrentlyValid(StoreOffer offer, DateTimeOffset now)
  {
    if (offer.DeletedAt != null) return false;
    if (!offer.IsActive) return false;
    if (offer.StartsAt.HasValue && now < offer.StartsAt.Value) return false;
    if (offer.EndsAt.HasValue && now > offer.EndsAt.Value) return false;
    return true;
  }

  public static int ComputeUnitDiscountCents(StoreOffer offer)
  {
    var price = offer.PriceCents;

    var t = (offer.DiscountType ?? "").Trim().ToUpperInvariant();
    if (t == DiscountTypes.None) return 0;

    if (t == DiscountTypes.Flat)
      return Math.Clamp(offer.DiscountCents ?? 0, 0, price);

    if (t == DiscountTypes.Percent)
    {
      var bps = offer.DiscountPercentBps ?? 0;
      if (bps <= 0) return 0;

      // floor division => deterministic
      var pctDiscount = (price * bps) / 10000;
      return Math.Clamp(pctDiscount, 0, price);
    }

    return 0;
  }
}
