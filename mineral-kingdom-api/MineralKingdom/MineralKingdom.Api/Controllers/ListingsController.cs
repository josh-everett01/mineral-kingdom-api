using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Public;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/listings")]
[AllowAnonymous]
public sealed class ListingsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  public ListingsController(MineralKingdomDbContext db) => _db = db;

  private sealed record BrowseRow(
    Guid Id,
    string Title,
    string Slug,
    string Href,
    string? PrimaryImageUrl,
    string? PrimaryMineral,
    string? LocalityDisplay,
    string? SizeClass,
    bool IsFluorescent,
    string ListingType,
    int? PriceCents,
    int? EffectivePriceCents,
    int? CurrentBidCents,
    DateTimeOffset? EndsAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

  public sealed record MediaDto(
    Guid Id,
    string MediaType,
    string Url,
    int SortOrder,
    bool IsPrimary,
    string? Caption);

  public sealed record ListingDto(
    Guid Id,
    string? Title,
    string? Description,
    string Status,
    Guid? PrimaryMineralId,
    string? PrimaryMineral,
    string? LocalityDisplay,
    string? CountryCode,
    string? SizeClass,
    bool IsFluorescent,
    string? FluorescenceNotes,
    string? ConditionNotes,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    int? WeightGrams,
    DateTimeOffset? PublishedAt,
    List<MediaDto> Media
  );

  [HttpGet]
  public async Task<ActionResult<ListingBrowseResponseDto>> Browse(
    [FromQuery] string? listingType,
    [FromQuery] string? mineralType,
    [FromQuery] string? sizeClass,
    [FromQuery] bool? fluorescent,
    [FromQuery] int? minPrice,
    [FromQuery] int? maxPrice,
    [FromQuery] string? sort,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 24,
    CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var normalizedType = NormalizeListingType(listingType);
    var normalizedSort = NormalizeSort(sort);
    var normalizedPage = page < 1 ? 1 : page;
    var normalizedPageSize = Math.Clamp(pageSize, 1, 60);

    var publishedListings = _db.Listings
      .AsNoTracking()
      .Where(x => x.Status == ListingStatuses.Published);

    var activeOffers = _db.StoreOffers
      .AsNoTracking()
      .Where(o =>
        o.DeletedAt == null &&
        o.IsActive &&
        (o.StartsAt == null || o.StartsAt <= now) &&
        (o.EndsAt == null || o.EndsAt >= now));

    var liveAuctions = _db.Auctions
      .AsNoTracking()
      .Where(a => a.Status == AuctionStatuses.Live || a.Status == AuctionStatuses.Closing);

    var listingRows = await (
      from listing in publishedListings
      join mineral in _db.Minerals.AsNoTracking() on listing.PrimaryMineralId equals mineral.Id into mineralJoin
      from mineral in mineralJoin.DefaultIfEmpty()
      select new
      {
        listing.Id,
        listing.Title,
        listing.PublishedAt,
        listing.CreatedAt,
        PrimaryMineral = mineral != null ? mineral.Name : null,
        listing.LocalityDisplay,
        listing.SizeClass,
        listing.IsFluorescent
      })
      .ToListAsync(ct);

    var listingIds = listingRows.Select(x => x.Id).ToList();

    var mediaRows = await _db.ListingMedia
      .AsNoTracking()
      .Where(m => listingIds.Contains(m.ListingId) && m.Status == ListingMediaStatuses.Ready && m.DeletedAt == null)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new { m.ListingId, m.Url })
      .ToListAsync(ct);

    var primaryImageByListing = mediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Url);

    var offerRows = await (
      from offer in activeOffers
      where listingIds.Contains(offer.ListingId)
      select new
      {
        offer.ListingId,
        offer.PriceCents,
        offer.DiscountType,
        offer.DiscountCents,
        offer.DiscountPercentBps
      })
      .ToListAsync(ct);

    var auctionRows = await (
      from auction in liveAuctions
      where listingIds.Contains(auction.ListingId)
      select new
      {
        auction.ListingId,
        auction.CurrentPriceCents,
        EndsAt = auction.ClosingWindowEnd ?? auction.CloseTime
      })
      .ToListAsync(ct);

    var offerByListingId = offerRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.First());

    var auctionByListingId = auctionRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.OrderBy(x => x.EndsAt).First());

    var publicRows = listingRows
      .Select(listing =>
      {
        var hasStore = offerByListingId.TryGetValue(listing.Id, out var offer);
        var hasAuction = auctionByListingId.TryGetValue(listing.Id, out var auction);

        if (!hasStore && !hasAuction)
          return null;

        var resolvedListingType = hasAuction ? "AUCTION" : "STORE";
        var priceCents = hasStore ? offer!.PriceCents : (int?)null;
        var effectivePriceCents = hasStore
          ? MineralKingdom.Contracts.Store.DiscountPricing.ComputeEffectivePriceCents(
            offer!.PriceCents,
            offer.DiscountType,
            offer.DiscountCents,
            offer.DiscountPercentBps)
          : (int?)null;
        var title = listing.Title ?? "Untitled listing";

        return new BrowseRow(
          Id: listing.Id,
          Title: title,
          Slug: PublicListingLinks.BuildSlug(title),
          Href: PublicListingLinks.BuildHref(listing.Id, title),
          PrimaryImageUrl: primaryImageByListing.GetValueOrDefault(listing.Id),
          PrimaryMineral: listing.PrimaryMineral,
          LocalityDisplay: listing.LocalityDisplay,
          SizeClass: listing.SizeClass,
          IsFluorescent: listing.IsFluorescent,
          ListingType: resolvedListingType,
          PriceCents: priceCents,
          EffectivePriceCents: effectivePriceCents,
          CurrentBidCents: hasAuction ? auction!.CurrentPriceCents : null,
          EndsAt: hasAuction ? auction!.EndsAt : null,
          PublishedAt: listing.PublishedAt,
          CreatedAt: listing.CreatedAt);
      })
      .Where(x => x is not null)
      .Cast<BrowseRow>()
      .ToList();

    var filteredItems = publicRows.AsEnumerable();

    if (normalizedType is not null)
      filteredItems = filteredItems.Where(x => string.Equals(x.ListingType, normalizedType, StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrWhiteSpace(mineralType))
      filteredItems = filteredItems.Where(x => string.Equals(x.PrimaryMineral, mineralType, StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrWhiteSpace(sizeClass))
      filteredItems = filteredItems.Where(x => string.Equals(x.SizeClass, sizeClass, StringComparison.OrdinalIgnoreCase));

    if (fluorescent == true)
      filteredItems = filteredItems.Where(x => x.IsFluorescent);

    if (minPrice.HasValue)
      filteredItems = filteredItems.Where(x => GetComparablePriceOrNull(x).HasValue && GetComparablePriceOrNull(x)!.Value >= minPrice.Value);

    if (maxPrice.HasValue)
      filteredItems = filteredItems.Where(x => GetComparablePriceOrNull(x).HasValue && GetComparablePriceOrNull(x)!.Value <= maxPrice.Value);

    filteredItems = normalizedSort switch
    {
      "price_asc" => filteredItems
        .OrderBy(x => GetComparablePriceOrNull(x) is null)
        .ThenBy(x => GetComparablePriceOrNull(x)),
      "price_desc" => filteredItems
        .OrderBy(x => GetComparablePriceOrNull(x) is null)
        .ThenByDescending(x => GetComparablePriceOrNull(x)),
      "ending_soon" => filteredItems
        .OrderBy(x => x.EndsAt is null)
        .ThenBy(x => x.EndsAt)
        .ThenByDescending(GetComparableRecencyTicks),
      _ => filteredItems.OrderByDescending(GetComparableRecencyTicks)
    };

    var filteredList = filteredItems.ToList();
    var total = filteredList.Count;
    var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)normalizedPageSize);

    var pageItems = filteredList
      .Skip((normalizedPage - 1) * normalizedPageSize)
      .Take(normalizedPageSize)
      .ToList();

    var availableFilters = new ListingBrowseAvailableFiltersDto(
      MineralTypes: publicRows
        .Select(x => x.PrimaryMineral)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .Cast<string>()
        .ToList(),
      SizeClasses: publicRows
        .Select(x => x.SizeClass)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .Cast<string>()
        .ToList());

    return Ok(new ListingBrowseResponseDto(
      Items: pageItems.Select(MapToBrowseItem).ToList(),
      Page: normalizedPage,
      PageSize: normalizedPageSize,
      Total: total,
      TotalPages: totalPages,
      AvailableFilters: availableFilters));
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<ListingDto>> Get(Guid id, CancellationToken ct)
  {
    var listing = await (
      from l in _db.Listings.AsNoTracking()
      join m in _db.Minerals.AsNoTracking() on l.PrimaryMineralId equals m.Id into mineralJoin
      from mineral in mineralJoin.DefaultIfEmpty()
      where l.Id == id
      select new
      {
        Listing = l,
        PrimaryMineral = mineral != null ? mineral.Name : null
      })
      .SingleOrDefaultAsync(ct);

    if (listing is null) return NotFound();

    if (!string.Equals(listing.Listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return NotFound();

    var media = await _db.ListingMedia.AsNoTracking()
      .Where(x => x.ListingId == id && x.Status == ListingMediaStatuses.Ready && x.DeletedAt == null)
      .OrderByDescending(x => x.IsPrimary)
      .ThenBy(x => x.SortOrder)
      .Select(x => new MediaDto(x.Id, x.MediaType, x.Url, x.SortOrder, x.IsPrimary, x.Caption))
      .ToListAsync(ct);

    return Ok(new ListingDto(
      listing.Listing.Id,
      listing.Listing.Title,
      listing.Listing.Description,
      listing.Listing.Status,
      listing.Listing.PrimaryMineralId,
      listing.PrimaryMineral,
      listing.Listing.LocalityDisplay,
      listing.Listing.CountryCode,
      listing.Listing.SizeClass,
      listing.Listing.IsFluorescent,
      listing.Listing.FluorescenceNotes,
      listing.Listing.ConditionNotes,
      listing.Listing.LengthCm,
      listing.Listing.WidthCm,
      listing.Listing.HeightCm,
      listing.Listing.WeightGrams,
      listing.Listing.PublishedAt,
      media
    ));
  }

  [HttpGet("{id:guid}/auction")]
  public async Task<ActionResult<AuctionRealtimeSnapshot>> GetAuctionForListing(Guid id, CancellationToken ct)
  {
    var isPublished = await _db.Listings.AsNoTracking()
      .AnyAsync(x => x.Id == id && x.Status == ListingStatuses.Published, ct);

    if (!isPublished)
      return NotFound();

    var auction = await _db.Auctions.AsNoTracking()
      .Where(x =>
        x.ListingId == id &&
        (x.Status == AuctionStatuses.Live || x.Status == AuctionStatuses.Closing))
      .OrderBy(x => x.ClosingWindowEnd ?? x.CloseTime)
      .FirstOrDefaultAsync(ct);

    if (auction is null)
      return NotFound();

    var reserveMet = auction.ReservePriceCents is not null ? auction.ReserveMet : (bool?)null;
    var minNext = auction.BidCount <= 0
      ? auction.StartingPriceCents
      : BidIncrementTable.MinToBeatCents(auction.CurrentPriceCents);

    return Ok(new AuctionRealtimeSnapshot(
      AuctionId: auction.Id,
      CurrentPriceCents: auction.CurrentPriceCents,
      BidCount: auction.BidCount,
      ReserveMet: reserveMet,
      Status: auction.Status,
      ClosingWindowEnd: auction.ClosingWindowEnd,
      MinimumNextBidCents: minNext
    ));
  }

  private static string? NormalizeListingType(string? listingType)
  {
    if (string.IsNullOrWhiteSpace(listingType))
      return null;

    return listingType.Trim().ToUpperInvariant() switch
    {
      "STORE" => "STORE",
      "AUCTION" => "AUCTION",
      _ => null
    };
  }

  private static string NormalizeSort(string? sort)
  {
    if (string.IsNullOrWhiteSpace(sort))
      return "newest";

    return sort.Trim().ToLowerInvariant() switch
    {
      "price_asc" => "price_asc",
      "price_desc" => "price_desc",
      "ending_soon" => "ending_soon",
      _ => "newest"
    };
  }

  private static ListingBrowseItemDto MapToBrowseItem(BrowseRow row)
    => new(
      Id: row.Id,
      Slug: row.Slug,
      Href: row.Href,
      Title: row.Title,
      PrimaryImageUrl: row.PrimaryImageUrl,
      PrimaryMineral: row.PrimaryMineral,
      LocalityDisplay: row.LocalityDisplay,
      SizeClass: row.SizeClass,
      IsFluorescent: row.IsFluorescent,
      ListingType: row.ListingType,
      PriceCents: row.PriceCents,
      EffectivePriceCents: row.EffectivePriceCents,
      CurrentBidCents: row.CurrentBidCents,
      EndsAt: row.EndsAt);

  private static long GetComparableRecencyTicks(BrowseRow item)
    => (item.PublishedAt ?? item.CreatedAt).UtcDateTime.Ticks;

  private static int? GetComparablePriceOrNull(BrowseRow item)
    => item.ListingType == "AUCTION"
      ? item.CurrentBidCents
      : item.EffectivePriceCents ?? item.PriceCents;
}