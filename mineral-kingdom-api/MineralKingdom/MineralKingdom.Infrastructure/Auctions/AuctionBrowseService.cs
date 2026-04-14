using System.Text;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionBrowseService
{
  private readonly MineralKingdomDbContext _db;

  public AuctionBrowseService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public async Task<AuctionBrowseResponseDto> GetPublicBrowseAsync(
    DateTimeOffset now,
    CancellationToken ct)
  {
    var visibleAuctions = _db.Auctions
      .AsNoTracking()
      .Where(a =>
        a.Status == AuctionStatuses.Live ||
        a.Status == AuctionStatuses.Closing ||
        a.Status == AuctionStatuses.Scheduled);

    var publishedListings = _db.Listings
      .AsNoTracking()
      .Where(l => l.Status == ListingStatuses.Published);

    var auctionListingRows = await (
      from auction in visibleAuctions
      join listing in publishedListings on auction.ListingId equals listing.Id
      select new
      {
        AuctionId = auction.Id,
        ListingId = listing.Id,
        Title = listing.Title ?? "Untitled listing",
        listing.LocalityDisplay,
        listing.SizeClass,
        listing.IsFluorescent,
        auction.CurrentPriceCents,
        auction.StartingPriceCents,
        auction.BidCount,
        auction.StartTime,
        ClosingTimeUtc = auction.ClosingWindowEnd ?? auction.CloseTime,
        auction.Status
      })
      .OrderBy(x => x.Status == AuctionStatuses.Scheduled ? 1 : 0)
      .ThenBy(x => x.Status == AuctionStatuses.Scheduled ? x.StartTime : x.ClosingTimeUtc)
      .ThenBy(x => x.Title)
      .ToListAsync(ct);

    var listingIds = auctionListingRows
      .Select(x => x.ListingId)
      .Distinct()
      .ToList();

    var mediaRows = await _db.ListingMedia
      .AsNoTracking()
      .Where(m =>
        listingIds.Contains(m.ListingId) &&
        m.Status == ListingMediaStatuses.Ready &&
        m.DeletedAt == null)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new
      {
        m.ListingId,
        m.Url
      })
      .ToListAsync(ct);

    var primaryImageByListing = mediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Url);

    var items = auctionListingRows
      .Select(x =>
      {
        var slug = BuildSlug(x.Title);

        return new AuctionBrowseItemDto(
          Id: x.AuctionId,
          ListingId: x.ListingId,
          Title: x.Title,
          Slug: slug,
          Href: $"/auctions/{x.AuctionId}",
          PrimaryImageUrl: primaryImageByListing.GetValueOrDefault(x.ListingId),
          LocalityDisplay: x.LocalityDisplay,
          SizeClass: x.SizeClass,
          IsFluorescent: x.IsFluorescent,
          CurrentPriceCents: x.CurrentPriceCents,
          StartingPriceCents: x.StartingPriceCents,
          BidCount: x.BidCount,
          StartTimeUtc: x.StartTime,
          ClosingTimeUtc: x.ClosingTimeUtc,
          Status: x.Status
        );
      })
      .ToList();

    return new AuctionBrowseResponseDto(
      Items: items,
      Total: items.Count,
      ServerTimeUtc: now
    );
  }

  private static string BuildSlug(string? title)
  {
    if (string.IsNullOrWhiteSpace(title))
      return "untitled-listing";

    var normalized = title.Trim().ToLowerInvariant();
    var sb = new StringBuilder(normalized.Length);
    var previousWasDash = false;

    foreach (var ch in normalized)
    {
      if (char.IsLetterOrDigit(ch))
      {
        sb.Append(ch);
        previousWasDash = false;
        continue;
      }

      if (previousWasDash)
        continue;

      sb.Append('-');
      previousWasDash = true;
    }

    var slug = sb.ToString().Trim('-');
    return string.IsNullOrWhiteSpace(slug) ? "untitled-listing" : slug;
  }
}