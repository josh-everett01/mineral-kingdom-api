using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/store/offers")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminStoreOffersController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminStoreOffersController(MineralKingdomDbContext db) => _db = db;

  [HttpPost]
  public async Task<ActionResult<StoreOfferIdResponse>> Upsert([FromBody] UpsertStoreOfferRequest req, CancellationToken ct)
  {
    // ensure listing exists
    var listingExists = await _db.Listings.AsNoTracking().AnyAsync(x => x.Id == req.ListingId, ct);
    if (!listingExists) return NotFound(new { error = "LISTING_NOT_FOUND" });

    var (ok, err) = DiscountPricing.Validate(req.PriceCents, req.DiscountType, req.DiscountCents, req.DiscountPercentBps);
    if (!ok) return BadRequest(new { error = err });

    var now = DateTimeOffset.UtcNow;

    // single-offer-per-listing behavior (idempotent upsert)
    var offer = await _db.StoreOffers.SingleOrDefaultAsync(
      x => x.ListingId == req.ListingId && x.DeletedAt == null,
      ct);

    if (offer is null)
    {
      offer = new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = req.ListingId,
        CreatedAt = now
      };
      _db.StoreOffers.Add(offer);
    }

    offer.PriceCents = req.PriceCents;
    offer.DiscountType = (req.DiscountType ?? "").Trim().ToUpperInvariant();
    offer.DiscountCents = req.DiscountCents;
    offer.DiscountPercentBps = req.DiscountPercentBps;

    offer.IsActive = req.IsActive;
    offer.StartsAt = req.StartsAt;
    offer.EndsAt = req.EndsAt;

    offer.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return Ok(new StoreOfferIdResponse(offer.Id));
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<StoreOfferDto>> GetById(Guid id, CancellationToken ct)
  {
    var offer = await _db.StoreOffers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
    if (offer is null) return NotFound(new { error = "OFFER_NOT_FOUND" });

    var effective = DiscountPricing.ComputeEffectivePriceCents(
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps);

    return Ok(ToDto(offer, effective));
  }

  [HttpGet]
  public async Task<ActionResult<StoreOfferDto>> GetByListing([FromQuery] Guid listingId, CancellationToken ct)
  {
    var offer = await _db.StoreOffers.AsNoTracking()
      .SingleOrDefaultAsync(x => x.ListingId == listingId && x.DeletedAt == null, ct);

    if (offer is null) return NotFound(new { error = "OFFER_NOT_FOUND" });

    var effective = DiscountPricing.ComputeEffectivePriceCents(
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps);

    return Ok(ToDto(offer, effective));
  }

  private static StoreOfferDto ToDto(StoreOffer o, int effectivePriceCents) =>
    new(
      o.Id,
      o.ListingId,
      o.PriceCents,
      o.DiscountType,
      o.DiscountCents,
      o.DiscountPercentBps,
      o.IsActive,
      o.StartsAt,
      o.EndsAt,
      effectivePriceCents
    );
}
