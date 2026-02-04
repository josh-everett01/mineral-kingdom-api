using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/store/offers")]
public sealed class StoreOffersController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public StoreOffersController(MineralKingdomDbContext db) => _db = db;

  [HttpGet("{listingId:guid}")]
  public async Task<ActionResult<StoreOfferDto>> GetActiveForListing(Guid listingId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var offer = await _db.StoreOffers.AsNoTracking()
      .SingleOrDefaultAsync(x =>
          x.ListingId == listingId &&
          x.DeletedAt == null &&
          x.IsActive &&
          (x.StartsAt == null || x.StartsAt <= now) &&
          (x.EndsAt == null || x.EndsAt >= now),
        ct);

    if (offer is null) return NotFound(new { error = "OFFER_NOT_FOUND" });

    var (ok, err) = DiscountPricing.Validate(offer.PriceCents, offer.DiscountType, offer.DiscountCents, offer.DiscountPercentBps);
    if (!ok) return Conflict(new { error = err }); // defensive: DB contains invalid offer

    var effective = DiscountPricing.ComputeEffectivePriceCents(
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps);

    return Ok(new StoreOfferDto(
      offer.Id,
      offer.ListingId,
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps,
      offer.IsActive,
      offer.StartsAt,
      offer.EndsAt,
      effective
    ));
  }
}
