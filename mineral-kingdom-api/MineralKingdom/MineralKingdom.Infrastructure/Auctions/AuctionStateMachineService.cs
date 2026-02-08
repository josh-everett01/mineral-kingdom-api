using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionStateMachineService
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionBiddingService _bidding;

  // Keep these as constants so later stories can configure via options
  private static readonly TimeSpan ClosingWindowDuration = TimeSpan.FromMinutes(10);

  public AuctionStateMachineService(MineralKingdomDbContext db, AuctionBiddingService bidding)
  {
    _db = db;
    _bidding = bidding;
  }

  /// <summary>
  /// Advances a single auction forward if server rules apply.
  /// Returns true if a transition happened.
  /// </summary>
  public async Task<(bool Changed, string? Error)> AdvanceAuctionAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
  {
    var a = await _db.Auctions.SingleOrDefaultAsync(x => x.Id == auctionId, ct);
    if (a is null) return (false, "AUCTION_NOT_FOUND");

    return await AdvanceLoadedAuctionAsync(a, now, ct);
  }

  /// <summary>
  /// Advances all due auctions based on indexed fields (status + close time / closing window end).
  /// </summary>
  public async Task<int> AdvanceDueAuctionsAsync(DateTimeOffset now, CancellationToken ct)
  {
    // Find candidates. We'll advance deterministically in small batches later; keep simple for S5-1.
    var due = await _db.Auctions
      .Where(a =>
        (a.Status == AuctionStatuses.Live && a.CloseTime <= now) ||
        (a.Status == AuctionStatuses.Closing && a.ClosingWindowEnd != null && a.ClosingWindowEnd <= now))
      .ToListAsync(ct);

    var changed = 0;
    foreach (var a in due)
    {
      var (didChange, _) = await AdvanceLoadedAuctionAsync(a, now, ct);
      if (didChange) changed++;
    }

    if (changed > 0)
      await _db.SaveChangesAsync(ct);

    return changed;
  }

  private async Task<(bool Changed, string? Error)> AdvanceLoadedAuctionAsync(Auction a, DateTimeOffset now, CancellationToken ct)
  {
    // Only server rules can transition these states
    if (a.Status == AuctionStatuses.Live)
    {
      if (a.CloseTime > now) return (false, null);

      var from = a.Status;
      a.Status = AuctionStatuses.Closing;
      a.ClosingWindowEnd ??= now.Add(ClosingWindowDuration);
      a.UpdatedAt = now;

      _db.AuctionBidEvents.Add(new AuctionBidEvent
      {
        Id = Guid.NewGuid(),
        AuctionId = a.Id,
        UserId = null,
        EventType = "STATUS_CHANGED",
        DataJson = $"{{\"from\":\"{from}\",\"to\":\"{a.Status}\"}}",
        ServerReceivedAt = now
      });

      await _db.SaveChangesAsync(ct);

      // Inject delayed bids immediately at start of closing
      var (ok, err) = await _bidding.InjectDelayedBidsAtClosingStartAsync(a.Id, now, ct);
      if (!ok) return (true, err); // state already changed; return err for visibility

      return (true, null);
    }

    if (a.Status == AuctionStatuses.Closing)
    {
      if (a.ClosingWindowEnd is null) return (false, "CLOSING_WINDOW_END_MISSING");
      if (a.ClosingWindowEnd > now) return (false, null);

      var from = a.Status;

      // Branch per Sprint 5 spec
      if (a.ReserveMet && a.BidCount > 0)
        a.Status = AuctionStatuses.ClosedWaitingOnPayment;
      else
        a.Status = AuctionStatuses.ClosedNotSold;

      a.UpdatedAt = now;

      _db.AuctionBidEvents.Add(new AuctionBidEvent
      {
        Id = Guid.NewGuid(),
        AuctionId = a.Id,
        UserId = null,
        EventType = "STATUS_CHANGED",
        DataJson = $"{{\"from\":\"{from}\",\"to\":\"{a.Status}\"}}",
        ServerReceivedAt = now
      });

      await _db.SaveChangesAsync(ct);
      return (true, null);
    }

    // other states are terminal in S5-1
    return (false, null);
  }
}
