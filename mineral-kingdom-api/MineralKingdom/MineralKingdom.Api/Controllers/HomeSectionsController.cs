using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Public;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Home;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/home")]
[AllowAnonymous]
public sealed class HomeSectionsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public HomeSectionsController(MineralKingdomDbContext db) => _db = db;

  [HttpGet("sections")]
  public async Task<ActionResult<HomeSectionsDto>> GetSections(CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    const int featuredLimit = 6;
    const int auctionLimit = 6;
    const int newArrivalsLimit = 6;

    var activeOffers = _db.StoreOffers
      .AsNoTracking()
      .Where(o =>
        o.DeletedAt == null &&
        o.IsActive &&
        (o.StartsAt == null || o.StartsAt <= now) &&
        (o.EndsAt == null || o.EndsAt >= now));

    var publishedListings = _db.Listings
      .AsNoTracking()
      .Where(l => l.Status == ListingStatuses.Published);

    var readyMedia = _db.ListingMedia
      .AsNoTracking()
      .Where(m => m.Status == ListingMediaStatuses.Ready && m.DeletedAt == null);

    var offerBackedListingsRows = await (
      from listing in publishedListings
      join offer in activeOffers on listing.Id equals offer.ListingId
      select new
      {
        listing.Id,
        listing.Title,
        listing.PublishedAt,
        listing.CreatedAt,
        offer.PriceCents,
        offer.DiscountType,
        offer.DiscountCents,
        offer.DiscountPercentBps
      })
      .ToListAsync(ct);

    var offerBackedListings = offerBackedListingsRows
      .Select(x => new
      {
        x.Id,
        x.Title,
        x.PublishedAt,
        x.CreatedAt,
        OfferPriceCents = x.PriceCents,
        EffectivePriceCents = MineralKingdom.Contracts.Store.DiscountPricing.ComputeEffectivePriceCents(
          x.PriceCents,
          x.DiscountType,
          x.DiscountCents,
          x.DiscountPercentBps)
      })
      .ToList();

    var listingIds = offerBackedListings.Select(x => x.Id).Distinct().ToList();

    var mediaLookup = await readyMedia
      .Where(m => listingIds.Contains(m.ListingId))
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new { m.ListingId, m.Url, m.IsPrimary, m.SortOrder })
      .ToListAsync(ct);

    var primaryImageByListing = mediaLookup
      .GroupBy(x => x.ListingId)
      .ToDictionary(
        g => g.Key,
        g => g.FirstOrDefault()?.Url
      );

    var featuredListings = offerBackedListings
      .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
      .Take(featuredLimit)
      .Select(x => new HomeSectionItemDto(
        ListingId: x.Id,
        AuctionId: null,
        Title: x.Title ?? "Untitled listing",
        PrimaryImageUrl: primaryImageByListing.GetValueOrDefault(x.Id),
        PriceCents: x.OfferPriceCents,
        EffectivePriceCents: x.EffectivePriceCents,
        CurrentBidCents: null,
        EndsAt: null,
        Href: PublicListingLinks.BuildHref(x.Id, x.Title)
      ))
      .ToList();

    var newArrivals = offerBackedListings
      .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
      .Take(newArrivalsLimit)
      .Select(x => new HomeSectionItemDto(
        ListingId: x.Id,
        AuctionId: null,
        Title: x.Title ?? "Untitled listing",
        PrimaryImageUrl: primaryImageByListing.GetValueOrDefault(x.Id),
        PriceCents: x.OfferPriceCents,
        EffectivePriceCents: x.EffectivePriceCents,
        CurrentBidCents: null,
        EndsAt: null,
        Href: PublicListingLinks.BuildHref(x.Id, x.Title)
      ))
      .ToList();

    var endingSoonRows = await (
      from auction in _db.Auctions.AsNoTracking()
      join listing in publishedListings on auction.ListingId equals listing.Id
      where auction.Status == AuctionStatuses.Live || auction.Status == AuctionStatuses.Closing
      select new
      {
        AuctionId = auction.Id,
        ListingId = listing.Id,
        listing.Title,
        EffectiveEnd = auction.ClosingWindowEnd ?? auction.CloseTime,
        auction.CurrentPriceCents
      })
      .OrderBy(x => x.EffectiveEnd)
      .Take(auctionLimit)
      .ToListAsync(ct);

    var endingSoonAuctions = endingSoonRows
      .Select(x => new HomeSectionItemDto(
        ListingId: x.ListingId,
        AuctionId: x.AuctionId,
        Title: x.Title ?? "Untitled auction",
        PrimaryImageUrl: primaryImageByListing.GetValueOrDefault(x.ListingId),
        PriceCents: null,
        EffectivePriceCents: null,
        CurrentBidCents: x.CurrentPriceCents,
        EndsAt: x.EffectiveEnd,
        Href: $"/auctions/{x.AuctionId}"
      ))
      .ToList();

    var dto = new HomeSectionsDto(
      FeaturedListings: new HomeSectionDto(
        Title: "Featured Listings",
        BrowseHref: "/shop",
        Count: featuredListings.Count,
        Items: featuredListings
      ),
      EndingSoonAuctions: new HomeSectionDto(
        Title: "Auctions Ending Soon",
        BrowseHref: "/auctions",
        Count: endingSoonAuctions.Count,
        Items: endingSoonAuctions
      ),
      NewArrivals: new HomeSectionDto(
        Title: "New Arrivals",
        BrowseHref: "/shop",
        Count: newArrivals.Count,
        Items: newArrivals
      )
    );

    return Ok(dto);
  }
}