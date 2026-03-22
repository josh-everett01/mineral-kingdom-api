using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionDetailService
{
  private readonly MineralKingdomDbContext _db;

  public AuctionDetailService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public async Task<AuctionDetailDto?> GetPublicDetailAsync(Guid auctionId, CancellationToken ct)
  {
    var row = await (
      from auction in _db.Auctions.AsNoTracking()
      join listing in _db.Listings.AsNoTracking() on auction.ListingId equals listing.Id
      where auction.Id == auctionId
      select new
      {
        AuctionId = auction.Id,
        ListingId = listing.Id,
        Title = listing.Title ?? "Untitled listing",
        listing.Description,
        auction.Status,
        auction.CurrentPriceCents,
        auction.BidCount,
        auction.ReservePriceCents,
        auction.ReserveMet,
        ClosingTimeUtc = auction.ClosingWindowEnd ?? auction.CloseTime
      })
      .SingleOrDefaultAsync(ct);

    if (row is null)
      return null;

    var media = await _db.ListingMedia
      .AsNoTracking()
      .Where(m =>
        m.ListingId == row.ListingId &&
        m.Status == ListingMediaStatuses.Ready &&
        m.DeletedAt == null)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new AuctionDetailMediaDto(
        m.Id,
        m.Url,
        m.IsPrimary,
        m.SortOrder))
      .ToListAsync(ct);

    var reserveMet = row.ReservePriceCents is not null ? row.ReserveMet : (bool?)null;

    var minimumNextBidCents = row.BidCount <= 0
      ? row.CurrentPriceCents
      : BidIncrementTable.MinToBeatCents(row.CurrentPriceCents);

    return new AuctionDetailDto(
      row.AuctionId,
      row.ListingId,
      row.Title,
      row.Description,
      row.Status,
      row.CurrentPriceCents,
      row.BidCount,
      reserveMet,
      row.ClosingTimeUtc,
      minimumNextBidCents,
      media
    );
  }
}