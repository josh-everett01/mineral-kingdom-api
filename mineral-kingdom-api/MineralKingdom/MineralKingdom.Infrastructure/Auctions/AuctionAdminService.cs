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

    // Ensure listing exists
    var listing = await _db.Listings
      .AsNoTracking()
      .SingleOrDefaultAsync(l => l.Id == req.ListingId, ct);

    if (listing is null) return (false, "LISTING_NOT_FOUND", null);

    // Ensure listing is eligible (basic guardrails)
    if (string.Equals(listing.Status, ListingStatuses.Sold, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(listing.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase))
      return (false, "LISTING_NOT_FOR_AUCTION", null);

    // Optional: enforce one active auction per listing (DRAFT/LIVE/CLOSING/WAITING)
    var hasExisting = await _db.Auctions.AsNoTracking().AnyAsync(a =>
      a.ListingId == req.ListingId &&
      a.Status != AuctionStatuses.ClosedPaid &&
      a.Status != AuctionStatuses.ClosedNotSold,
      ct);

    if (hasExisting) return (false, "AUCTION_ALREADY_EXISTS_FOR_LISTING", null);

    // Create DRAFT auction with server-owned derived fields
    var auction = new Auction
    {
      Id = Guid.NewGuid(),
      ListingId = req.ListingId,
      Status = AuctionStatuses.Draft,

      StartingPriceCents = req.StartingPriceCents,
      ReservePriceCents = req.ReservePriceCents,
      StartTime = null,
      CloseTime = req.CloseTime,
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
      DataJson = null,
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null, auction.Id);
  }

  public async Task<(bool Ok, string? Error)> StartAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
  {
    var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == auctionId, ct);
    if (auction is null) return (false, "AUCTION_NOT_FOUND");

    if (auction.Status != AuctionStatuses.Draft)
      return (false, "INVALID_STATUS");

    if (auction.CloseTime <= now)
      return (false, "CLOSE_TIME_IN_PAST");

    var listing = await _db.Listings.AsNoTracking().SingleAsync(l => l.Id == auction.ListingId, ct);
    if (string.Equals(listing.Status, ListingStatuses.Sold, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(listing.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase))
      return (false, "LISTING_NOT_FOR_AUCTION");

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
      DataJson = $"{{\"from\":\"{from}\",\"to\":\"{auction.Status}\"}}",
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }
}
