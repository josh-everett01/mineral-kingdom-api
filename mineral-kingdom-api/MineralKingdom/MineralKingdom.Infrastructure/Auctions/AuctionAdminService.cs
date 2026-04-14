using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionAdminService
{
  private readonly MineralKingdomDbContext _db;

  public AuctionAdminService(MineralKingdomDbContext db) => _db = db;

  public async Task<(bool Ok, string? Error, Guid? AuctionId)> CreateDraftAsync(
    CreateAuctionRequest req,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (req.StartingPriceCents <= 0) return (false, "INVALID_STARTING_PRICE", null);
    if (req.ReservePriceCents is not null && req.ReservePriceCents < req.StartingPriceCents)
      return (false, "INVALID_RESERVE_PRICE", null);
    if (req.QuotedShippingCents is not null && req.QuotedShippingCents < 0)
      return (false, "INVALID_QUOTED_SHIPPING", null);

    var launchMode = (req.LaunchMode ?? "").Trim().ToUpperInvariant();
    var timingMode = (req.TimingMode ?? "").Trim().ToUpperInvariant();

    if (!AuctionLaunchModes.IsValid(launchMode))
      return (false, "INVALID_LAUNCH_MODE", null);

    if (!AuctionTimingModes.IsValid(timingMode))
      return (false, "INVALID_TIMING_MODE", null);

    var listing = await _db.Listings
      .AsNoTracking()
      .SingleOrDefaultAsync(l => l.Id == req.ListingId, ct);

    if (listing is null) return (false, "LISTING_NOT_FOUND", null);

    if (!string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return (false, "LISTING_NOT_PUBLISHED", null);

    var hasExisting = await _db.Auctions.AsNoTracking().AnyAsync(a =>
      a.ListingId == req.ListingId &&
      a.Status != AuctionStatuses.ClosedPaid &&
      a.Status != AuctionStatuses.ClosedNotSold,
      ct);

    if (hasExisting) return (false, "AUCTION_ALREADY_EXISTS_FOR_LISTING", null);

    DateTimeOffset? startTime;
    DateTimeOffset closeTime;
    string status;

    switch (launchMode)
    {
      case AuctionLaunchModes.Draft:
        status = AuctionStatuses.Draft;
        startTime = req.StartTime;
        break;

      case AuctionLaunchModes.LaunchNow:
        status = AuctionStatuses.Live;
        startTime = now;
        break;

      case AuctionLaunchModes.Scheduled:
        status = AuctionStatuses.Scheduled;
        if (req.StartTime is null) return (false, "START_TIME_REQUIRED", null);
        if (req.StartTime.Value <= now) return (false, "START_TIME_MUST_BE_IN_FUTURE", null);
        startTime = req.StartTime.Value;
        break;

      default:
        return (false, "INVALID_LAUNCH_MODE", null);
    }

    if (timingMode == AuctionTimingModes.PresetDuration)
    {
      if (req.DurationHours is null || req.DurationHours <= 0)
        return (false, "DURATION_HOURS_REQUIRED", null);

      closeTime = startTime is not null
        ? startTime.Value.AddHours(req.DurationHours.Value)
        : now.AddHours(req.DurationHours.Value);
    }
    else
    {
      if (req.CloseTime is null) return (false, "CLOSE_TIME_REQUIRED", null);
      closeTime = req.CloseTime.Value;
    }

    if (closeTime <= now && status == AuctionStatuses.Live)
      return (false, "CLOSE_TIME_IN_PAST", null);

    if (startTime is not null && startTime.Value >= closeTime)
      return (false, "INVALID_START_AND_CLOSE_TIME", null);

    var auction = new Auction
    {
      Id = Guid.NewGuid(),
      ListingId = req.ListingId,
      Status = status,

      StartingPriceCents = req.StartingPriceCents,
      ReservePriceCents = req.ReservePriceCents,
      QuotedShippingCents = req.QuotedShippingCents,

      StartTime = startTime,
      CloseTime = closeTime,

      ClosingWindowEnd = null,

      CurrentPriceCents = req.StartingPriceCents,
      CurrentLeaderUserId = null,
      CurrentLeaderMaxCents = null,
      BidCount = 0,
      ReserveMet = false,

      RelistOfAuctionId = null,
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Auctions.Add(auction);

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = null,
      EventType = "AUCTION_CREATED",
      DataJson =
        $"{{\"listingId\":\"{auction.ListingId}\",\"status\":\"{auction.Status}\",\"startTime\":\"{auction.StartTime:o}\",\"closeTime\":\"{auction.CloseTime:o}\",\"startingPriceCents\":{auction.StartingPriceCents},\"reservePriceCents\":{(auction.ReservePriceCents?.ToString() ?? "null")},\"quotedShippingCents\":{(auction.QuotedShippingCents?.ToString() ?? "null")}}}",
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null, auction.Id);
  }

  public async Task<(bool Ok, string? Error)> StartAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
  {
    var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == auctionId, ct);
    if (auction is null) return (false, "AUCTION_NOT_FOUND");

    if (auction.Status != AuctionStatuses.Draft && auction.Status != AuctionStatuses.Scheduled)
      return (false, "INVALID_STATUS");

    if (auction.CloseTime <= now)
      return (false, "CLOSE_TIME_IN_PAST");

    var listing = await _db.Listings.AsNoTracking().SingleAsync(l => l.Id == auction.ListingId, ct);
    if (!string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return (false, "LISTING_NOT_PUBLISHED");

    auction.CurrentPriceCents = auction.StartingPriceCents;
    auction.CurrentLeaderUserId = null;
    auction.CurrentLeaderMaxCents = null;
    auction.BidCount = 0;
    auction.ReserveMet = false;
    auction.ClosingWindowEnd = null;

    var from = auction.Status;
    auction.Status = AuctionStatuses.Live;
    auction.StartTime = now;
    auction.UpdatedAt = now;

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = null,
      EventType = "STATUS_CHANGED",
      DataJson = $"{{\"from\":\"{from}\",\"to\":\"{auction.Status}\",\"reason\":\"ADMIN_START\"}}",
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> UpdateAsync(
    Guid auctionId,
    UpdateAuctionRequest req,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == auctionId, ct);
    if (auction is null) return (false, "AUCTION_NOT_FOUND");

    if (auction.Status != AuctionStatuses.Draft && auction.Status != AuctionStatuses.Scheduled)
      return (false, "AUCTION_UPDATE_NOT_ALLOWED_FOR_STATUS");

    var nextStartTime = req.StartTime ?? auction.StartTime;
    var nextCloseTime = req.CloseTime ?? auction.CloseTime;
    var nextStartingPrice = req.StartingPriceCents ?? auction.StartingPriceCents;
    var nextReserve = req.ReservePriceCents ?? auction.ReservePriceCents;
    var nextQuotedShipping = req.QuotedShippingCents ?? auction.QuotedShippingCents;

    if (nextStartingPrice <= 0) return (false, "INVALID_STARTING_PRICE");
    if (nextReserve is not null && nextReserve < nextStartingPrice)
      return (false, "INVALID_RESERVE_PRICE");
    if (nextQuotedShipping is not null && nextQuotedShipping < 0)
      return (false, "INVALID_QUOTED_SHIPPING");
    if (nextCloseTime <= now && auction.Status == AuctionStatuses.Live)
      return (false, "CLOSE_TIME_IN_PAST");
    if (nextStartTime is not null && nextStartTime.Value >= nextCloseTime)
      return (false, "INVALID_START_AND_CLOSE_TIME");

    if (auction.Status == AuctionStatuses.Scheduled && nextStartTime is not null && nextStartTime.Value <= now)
      return (false, "START_TIME_MUST_BE_IN_FUTURE");

    auction.StartTime = nextStartTime;
    auction.CloseTime = nextCloseTime;
    auction.StartingPriceCents = nextStartingPrice;
    auction.ReservePriceCents = nextReserve;
    auction.QuotedShippingCents = nextQuotedShipping;

    auction.CurrentPriceCents = nextStartingPrice;
    auction.CurrentLeaderUserId = null;
    auction.CurrentLeaderMaxCents = null;
    auction.BidCount = 0;
    auction.ReserveMet = false;
    auction.ClosingWindowEnd = null;
    auction.UpdatedAt = now;

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = null,
      EventType = "AUCTION_UPDATED",
      DataJson =
        $"{{\"startTime\":\"{auction.StartTime:o}\",\"closeTime\":\"{auction.CloseTime:o}\",\"startingPriceCents\":{auction.StartingPriceCents},\"reservePriceCents\":{(auction.ReservePriceCents?.ToString() ?? "null")},\"quotedShippingCents\":{(auction.QuotedShippingCents?.ToString() ?? "null")}}}",
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }
}