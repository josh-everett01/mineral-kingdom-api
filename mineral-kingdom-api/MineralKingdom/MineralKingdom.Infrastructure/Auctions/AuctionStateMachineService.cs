using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionStateMachineService
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionBiddingService _bidding;



  // Keep these as constants so later stories can configure via options
  private static readonly TimeSpan ClosingWindowDuration = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan RelistDelay = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan DefaultAuctionDuration = TimeSpan.FromHours(24);
  private static readonly TimeSpan DefaultAuctionPaymentWindow = TimeSpan.FromHours(48);



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
    var advanced = 0;

    var liveDue = await _db.Auctions
      .AsNoTracking()
      .Where(a => a.Status == AuctionStatuses.Live && a.CloseTime <= now)
      .Select(a => a.Id)
      .ToListAsync(ct);

    foreach (var id in liveDue)
    {
      var (changed, _) = await AdvanceAuctionAsync(id, now, ct);
      if (changed) advanced++;
    }

    var closingDue = await _db.Auctions
      .AsNoTracking()
      .Where(a => a.Status == AuctionStatuses.Closing &&
                  a.ClosingWindowEnd != null &&
                  a.ClosingWindowEnd <= now)
      .Select(a => a.Id)
      .ToListAsync(ct);

    foreach (var id in closingDue)
    {
      var (changed, _) = await AdvanceAuctionAsync(id, now, ct);
      if (changed) advanced++;
    }

    var relistDue = await _db.Auctions
  .AsNoTracking()
  .Where(a =>
    a.Status == AuctionStatuses.ClosedNotSold &&
    a.RelistOfAuctionId == null &&                 // fast-path
    a.ReservePriceCents != null &&                 // only reserve auctions
    a.ReserveMet == false &&                       // reserve not met
    a.UpdatedAt <= now.Subtract(RelistDelay))
    .Select(a => a.Id)
    .ToListAsync(ct);

    foreach (var id in relistDue)
    {
      var (changed, _) = await TryRelistAuctionAsync(id, now, ct);
      if (changed) advanced++;
    }

    return advanced;
  }

  private async Task<(bool Changed, string? Error)> AdvanceLoadedAuctionAsync(Auction a, DateTimeOffset now, CancellationToken ct)
  {
    if (a.Status == AuctionStatuses.Live)
    {
      if (a.CloseTime > now) return (false, null);

      await using var tx = await _db.Database.BeginTransactionAsync(ct);

      // Reload + lock inside the transaction so we’re not acting on a stale entity
      var locked = await _db.Auctions
        .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {a.Id} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

      if (locked is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "AUCTION_NOT_FOUND");
      }

      // Another worker might have already pushed it forward
      if (locked.Status != AuctionStatuses.Live)
      {
        await tx.CommitAsync(ct);
        return (false, null);
      }

      if (locked.CloseTime > now)
      {
        await tx.CommitAsync(ct);
        return (false, null);
      }

      var from = locked.Status;
      locked.Status = AuctionStatuses.Closing;

      var proposed = now.Add(ClosingWindowDuration);
      if (locked.ClosingWindowEnd is null || locked.ClosingWindowEnd < proposed)
        locked.ClosingWindowEnd = proposed;

      locked.UpdatedAt = now;

      _db.AuctionBidEvents.Add(new AuctionBidEvent
      {
        Id = Guid.NewGuid(),
        AuctionId = locked.Id,
        UserId = null,
        EventType = "STATUS_CHANGED",
        DataJson = $"{{\"from\":\"{from}\",\"to\":\"{locked.Status}\"}}",
        ServerReceivedAt = now
      });

      await _db.SaveChangesAsync(ct);

      // Option A: no transaction inside InjectDelayedBids...
      var (ok, err) = await _bidding.InjectDelayedBidsAtClosingStartAsync(locked.Id, now, ct);
      if (!ok)
      {
        await tx.RollbackAsync(ct);
        return (false, err); // rollback keeps us atomic
      }

      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);

      return (true, null);
    }

    if (a.Status == AuctionStatuses.Closing)
    {
      await using var tx = await _db.Database.BeginTransactionAsync(ct);

      // Reload + lock inside transaction (same as LIVE->CLOSING)
      var locked = await _db.Auctions
        .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {a.Id} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

      if (locked is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "AUCTION_NOT_FOUND");
      }

      // Another worker may have moved it already
      if (locked.Status != AuctionStatuses.Closing)
      {
        await tx.CommitAsync(ct);
        return (false, null);
      }

      if (locked.ClosingWindowEnd is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "CLOSING_WINDOW_END_MISSING");
      }

      // Quiet-window rule: only close if the window has actually expired
      if (locked.ClosingWindowEnd > now)
      {
        await tx.CommitAsync(ct);
        return (false, null);
      }

      var from = locked.Status;

      // Deterministic resolution based on derived state
      if (locked.BidCount <= 0)
      {
        locked.Status = AuctionStatuses.ClosedNotSold;
      }
      else if (locked.ReservePriceCents is not null && locked.ReserveMet == false)
      {
        locked.Status = AuctionStatuses.ClosedNotSold;
      }
      else
      {
        locked.Status = AuctionStatuses.ClosedWaitingOnPayment;
        await EnsureUnpaidAuctionOrderExistsAsync(locked, now, ct);
      }

      locked.UpdatedAt = now;

      _db.AuctionBidEvents.Add(new AuctionBidEvent
      {
        Id = Guid.NewGuid(),
        AuctionId = locked.Id,
        UserId = null,
        EventType = "STATUS_CHANGED",
        DataJson = $"{{\"from\":\"{from}\",\"to\":\"{locked.Status}\"}}",
        ServerReceivedAt = now
      });

      try
      {
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (true, null);
      }
      catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
      {
        // If the unique violation is from UX_orders_AuctionId due to a race,
        // treat as idempotent success.
        await tx.RollbackAsync(ct);
        return (true, null);
      }
    }

    return (false, null);
  }

  private async Task<(bool Changed, string? Error)> TryRelistAuctionAsync(Guid oldAuctionId, DateTimeOffset now, CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // Lock the old auction row (prevents double relist from concurrent workers)
    var old = await _db.Auctions
      .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {oldAuctionId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (old is null)
    {
      await tx.RollbackAsync(ct);
      return (false, "AUCTION_NOT_FOUND");
    }

    if (old.Status != AuctionStatuses.ClosedNotSold)
    {
      await tx.CommitAsync(ct);
      return (false, null);
    }

    if (old.ReservePriceCents is null || old.ReserveMet)
    {
      // Not a reserve-not-met outcome => no relist per S5-3 rules
      await tx.CommitAsync(ct);
      return (false, null);
    }

    // Ensure delay window has elapsed (defensive: sweep already filtered)
    if (old.UpdatedAt > now.Subtract(RelistDelay))
    {
      await tx.CommitAsync(ct);
      return (false, null);
    }

    // Idempotency: do not create if already relisted
    var exists = await _db.Auctions
      .AsNoTracking()
      .AnyAsync(a => a.RelistOfAuctionId == old.Id, ct);

    if (exists)
    {
      await tx.CommitAsync(ct);
      return (false, null);
    }

    // Duration: prefer (Close - Start) if StartTime exists, else default
    var duration =
      old.StartTime is not null
        ? old.CloseTime - old.StartTime.Value
        : DefaultAuctionDuration;

    // Guard against weird/negative durations
    if (duration <= TimeSpan.Zero)
      duration = DefaultAuctionDuration;

    var newAuctionId = Guid.NewGuid();

    var relisted = new Auction
    {
      Id = newAuctionId,
      ListingId = old.ListingId,
      RelistOfAuctionId = old.Id,

      Status = AuctionStatuses.Live,

      StartingPriceCents = old.StartingPriceCents,
      ReservePriceCents = old.ReservePriceCents,

      StartTime = now,
      CloseTime = now.Add(duration),
      ClosingWindowEnd = null,

      CurrentPriceCents = old.StartingPriceCents,
      CurrentLeaderUserId = null,
      CurrentLeaderMaxCents = null,
      BidCount = 0,
      ReserveMet = false,

      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Auctions.Add(relisted);

    // Event on old
    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = old.Id,
      UserId = null,
      EventType = "RELIST_TRIGGERED",
      DataJson = $"{{\"newAuctionId\":\"{newAuctionId}\"}}",
      ServerReceivedAt = now
    });

    // Event on new
    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = newAuctionId,
      UserId = null,
      EventType = "AUCTION_RELISTED",
      DataJson = $"{{\"fromAuctionId\":\"{old.Id}\"}}",
      ServerReceivedAt = now
    });

    try
    {
      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return (true, null);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
    {
      await tx.RollbackAsync(ct);
      return (false, null);
    }
  }

  private async Task EnsureUnpaidAuctionOrderExistsAsync(Auction locked, DateTimeOffset now, CancellationToken ct)
  {
    // Only for sold path; winner must exist
    if (locked.CurrentLeaderUserId is null)
      throw new InvalidOperationException("Sold auction must have a winner (CurrentLeaderUserId).");

    // Idempotency: one order per auction (also enforced by UX_orders_AuctionId)
    var exists = await _db.Orders.AnyAsync(o => o.AuctionId == locked.Id, ct);
    if (exists) return;

    var total = locked.CurrentPriceCents;
    if (total <= 0)
      throw new InvalidOperationException("Sold auction must have a positive CurrentPriceCents.");

    var orderId = Guid.NewGuid();

    var order = new Order
    {
      Id = orderId,
      UserId = locked.CurrentLeaderUserId,
      GuestEmail = null,
      OrderNumber = GenerateOrderNumber(now),

      CheckoutHoldId = null,

      SourceType = "AUCTION",
      AuctionId = locked.Id,
      PaymentDueAt = now.Add(DefaultAuctionPaymentWindow),

      Status = "AWAITING_PAYMENT",
      PaidAt = null,

      SubtotalCents = total,
      DiscountTotalCents = 0,
      TotalCents = total,
      CurrencyCode = "USD",

      CreatedAt = now,
      UpdatedAt = now,
      Lines = new List<OrderLine>()
    };

    order.Lines.Add(new OrderLine
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      OfferId = null,
      ListingId = locked.ListingId,

      UnitPriceCents = total,
      UnitDiscountCents = 0,
      UnitFinalPriceCents = total,
      Quantity = 1,
      LineSubtotalCents = total,
      LineDiscountCents = 0,
      LineTotalCents = total,

      CreatedAt = now,
      UpdatedAt = now
    });

    _db.Orders.Add(order);
  }

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }
}
