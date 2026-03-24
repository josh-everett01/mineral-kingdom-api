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

  public async Task<AuctionDetailDto?> GetPublicDetailAsync(
    Guid auctionId,
    Guid? currentUserId,
    CancellationToken ct)
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
        auction.CurrentLeaderUserId,
        auction.CurrentLeaderMaxCents,
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

    bool? isCurrentUserLeading = null;
    bool? hasCurrentUserBid = null;
    int? currentUserMaxBidCents = null;
    string? currentUserBidState = null;

    bool? hasPendingDelayedBid = null;
    int? currentUserDelayedBidCents = null;
    string? currentUserDelayedBidStatus = null;

    if (currentUserId.HasValue)
    {
      var immediateBid = await _db.AuctionMaxBids
        .AsNoTracking()
        .Where(x => x.AuctionId == auctionId && x.UserId == currentUserId.Value)
        .Select(x => new
        {
          x.MaxBidCents,
          x.BidType,
          x.ReceivedAt
        })
        .SingleOrDefaultAsync(ct);

      var delayedBid = await _db.AuctionDelayedBids
        .AsNoTracking()
        .Where(x => x.AuctionId == auctionId && x.UserId == currentUserId.Value)
        .Select(x => new
        {
          x.MaxBidCents,
          x.Status,
          x.CreatedAt,
          x.UpdatedAt,
          x.CancelledAt,
          x.MootedAt,
          x.ActivatedAt
        })
        .SingleOrDefaultAsync(ct);

      var hasActiveImmediateBid = immediateBid is not null;
      var hasVisibleDelayedBid =
        delayedBid is not null &&
        !string.Equals(delayedBid.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);

      hasCurrentUserBid = hasActiveImmediateBid || hasVisibleDelayedBid;
      currentUserMaxBidCents = immediateBid?.MaxBidCents;

      isCurrentUserLeading = hasActiveImmediateBid && row.CurrentLeaderUserId == currentUserId.Value;

      if (!hasActiveImmediateBid)
      {
        currentUserBidState = "NONE";
      }
      else if (isCurrentUserLeading == true)
      {
        currentUserBidState = "LEADING";
      }
      else
      {
        currentUserBidState = "OUTBID";
      }

      if (!hasVisibleDelayedBid)
      {
        hasPendingDelayedBid = false;
        currentUserDelayedBidCents = null;
        currentUserDelayedBidStatus = "NONE";
      }
      else
      {
        hasPendingDelayedBid = true;
        currentUserDelayedBidCents = delayedBid!.MaxBidCents;

        if (string.Equals(delayedBid.Status, "ACTIVATED", StringComparison.OrdinalIgnoreCase))
        {
          currentUserDelayedBidStatus = "ACTIVATED";
        }
        else
        {
          var delayedBidMootByPrice = row.CurrentPriceCents >= delayedBid.MaxBidCents;
          var delayedBidMootByImmediateSupersession =
            immediateBid is not null &&
            immediateBid.MaxBidCents >= delayedBid.MaxBidCents;

          if (delayedBidMootByPrice || delayedBidMootByImmediateSupersession)
          {
            currentUserDelayedBidStatus = "MOOT";
          }
          else
          {
            currentUserDelayedBidStatus = "SCHEDULED";
          }
        }
      }
    }

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
      media,
      isCurrentUserLeading,
      hasCurrentUserBid,
      currentUserMaxBidCents,
      currentUserBidState,
      hasPendingDelayedBid,
      currentUserDelayedBidCents,
      currentUserDelayedBidStatus
    );
  }
}