using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Listings;
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
  public async Task<ActionResult<StoreOfferIdResponse>> Upsert(
    [FromBody] UpsertStoreOfferRequest req,
    CancellationToken ct)
  {
    var listing = await _db.Listings
      .SingleOrDefaultAsync(x => x.Id == req.ListingId, ct);

    if (listing is null)
      return NotFound(new { error = "LISTING_NOT_FOUND" });

    if (!string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "LISTING_NOT_ELIGIBLE" });

    var (ok, err) = DiscountPricing.Validate(
      req.PriceCents,
      req.DiscountType,
      req.DiscountCents,
      req.DiscountPercentBps);

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
    offer.DiscountType = NormalizeDiscountType(req.DiscountType);
    offer.DiscountCents = req.DiscountCents;
    offer.DiscountPercentBps = req.DiscountPercentBps;
    offer.IsActive = req.IsActive;
    offer.StartsAt = req.StartsAt;
    offer.EndsAt = req.EndsAt;
    offer.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return Ok(new StoreOfferIdResponse(offer.Id));
  }

  [HttpPatch("{id:guid}")]
  public async Task<ActionResult<StoreOfferDto>> Update(
    Guid id,
    [FromBody] UpdateStoreOfferRequest req,
    CancellationToken ct)
  {
    var offer = await _db.StoreOffers
      .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);

    if (offer is null)
      return NotFound(new { error = "OFFER_NOT_FOUND" });

    var listing = await _db.Listings
      .SingleOrDefaultAsync(x => x.Id == offer.ListingId, ct);

    if (listing is null)
      return NotFound(new { error = "LISTING_NOT_FOUND" });

    if (!string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "LISTING_NOT_ELIGIBLE" });

    var normalizedDiscountType = NormalizeDiscountType(req.DiscountType);

    var (ok, err) = DiscountPricing.Validate(
      req.PriceCents,
      normalizedDiscountType,
      req.DiscountCents,
      req.DiscountPercentBps);

    if (!ok) return BadRequest(new { error = err });

    offer.PriceCents = req.PriceCents;
    offer.DiscountType = normalizedDiscountType;
    offer.DiscountCents = req.DiscountCents;
    offer.DiscountPercentBps = req.DiscountPercentBps;
    offer.IsActive = req.IsActive;
    offer.StartsAt = req.StartsAt;
    offer.EndsAt = req.EndsAt;
    offer.UpdatedAt = DateTimeOffset.UtcNow;

    await _db.SaveChangesAsync(ct);

    var effective = DiscountPricing.ComputeEffectivePriceCents(
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps);

    return Ok(ToDto(offer, effective));
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<StoreOfferDto>> GetById(Guid id, CancellationToken ct)
  {
    var offer = await _db.StoreOffers.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);

    if (offer is null)
      return NotFound(new { error = "OFFER_NOT_FOUND" });

    var effective = DiscountPricing.ComputeEffectivePriceCents(
      offer.PriceCents,
      offer.DiscountType,
      offer.DiscountCents,
      offer.DiscountPercentBps);

    return Ok(ToDto(offer, effective));
  }

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<AdminStoreOfferListItemDto>>> List(
    [FromQuery] Guid? listingId,
    CancellationToken ct)
  {
    var query =
      from offer in _db.StoreOffers.AsNoTracking()
      join listing in _db.Listings.AsNoTracking()
        on offer.ListingId equals listing.Id
      where offer.DeletedAt == null
      select new
      {
        offer.Id,
        offer.ListingId,
        ListingTitle = listing.Title,
        ListingStatus = listing.Status,
        offer.PriceCents,
        offer.DiscountType,
        offer.DiscountCents,
        offer.DiscountPercentBps,
        offer.IsActive,
        offer.StartsAt,
        offer.EndsAt,
        offer.CreatedAt,
        offer.UpdatedAt
      };

    if (listingId.HasValue)
    {
      query = query.Where(x => x.ListingId == listingId.Value);
    }

    var rows = await query
      .OrderByDescending(x => x.UpdatedAt)
      .ThenByDescending(x => x.CreatedAt)
      .ToListAsync(ct);

    var result = rows
      .Select(x => new AdminStoreOfferListItemDto(
        x.Id,
        x.ListingId,
        x.ListingTitle,
        x.ListingStatus,
        x.PriceCents,
        x.DiscountType,
        x.DiscountCents,
        x.DiscountPercentBps,
        x.IsActive,
        x.StartsAt,
        x.EndsAt,
        DiscountPricing.ComputeEffectivePriceCents(
          x.PriceCents,
          x.DiscountType,
          x.DiscountCents,
          x.DiscountPercentBps),
        x.CreatedAt,
        x.UpdatedAt
      ))
      .ToList();

    return Ok(result);
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

  private static string NormalizeDiscountType(string? value) =>
    string.IsNullOrWhiteSpace(value)
      ? DiscountTypes.None
      : value.Trim().ToUpperInvariant();
}