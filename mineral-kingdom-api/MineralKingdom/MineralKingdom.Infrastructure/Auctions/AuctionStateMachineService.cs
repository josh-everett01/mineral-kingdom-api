using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionStateMachineService
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionBiddingService _bidding;
  private readonly IAuctionRealtimeNotifier _notifier;

  // Keep these as constants so later stories can configure via options
  private static readonly TimeSpan ClosingWindowDuration = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan RelistDelay = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan DefaultAuctionDuration = TimeSpan.FromHours(24);
  private static readonly TimeSpan DefaultAuctionPaymentWindow = TimeSpan.FromHours(48);

  private readonly EmailOutboxService _emails;

  public AuctionStateMachineService(
    MineralKingdomDbContext db,
    AuctionBiddingService bidding,
    IAuctionRealtimeNotifier notifier,
    EmailOutboxService emails)
  {
    _db = db;
    _bidding = bidding;
    _notifier = notifier;
    _emails = emails;
  }

  public async Task<(bool Changed, string? Error)> AdvanceAuctionAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
  {
    var a = await _db.Auctions.SingleOrDefaultAsync(x => x.Id == auctionId, ct);
    if (a is null) return (false, "AUCTION_NOT_FOUND");

    return await AdvanceLoadedAuctionAsync(a, now, ct);
  }

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
        a.RelistOfAuctionId == null &&
        a.ReservePriceCents != null &&
        a.ReserveMet == false &&
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

      var locked = await _db.Auctions
        .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {a.Id} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

      if (locked is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "AUCTION_NOT_FOUND");
      }

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

      var (ok, err) = await _bidding.InjectDelayedBidsAtClosingStartAsync(locked.Id, now, ct);
      if (!ok)
      {
        await tx.RollbackAsync(ct);
        return (false, err);
      }

      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);

      try { await _notifier.NotifyAuctionChangedAsync(locked.Id, ct); } catch { }

      return (true, null);
    }

    if (a.Status == AuctionStatuses.Closing)
    {
      await using var tx = await _db.Database.BeginTransactionAsync(ct);

      var locked = await _db.Auctions
        .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {a.Id} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

      if (locked is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "AUCTION_NOT_FOUND");
      }

      if (locked.Status != AuctionStatuses.Closing)
      {
        await tx.CommitAsync(ct);

        if (locked.Status == AuctionStatuses.ClosedWaitingOnPayment)
        {
          try
          {
            var order = await _db.Orders.AsNoTracking()
              .SingleOrDefaultAsync(o => o.AuctionId == locked.Id, ct);

            if (order?.UserId is Guid winnerUserId)
            {
              var toEmail = await _db.Users.AsNoTracking()
                .Where(u => u.Id == winnerUserId)
                .Select(u => u.Email)
                .SingleOrDefaultAsync(ct);

              if (!string.IsNullOrWhiteSpace(toEmail))
              {
                var payload =
                  $"{{\"auctionId\":\"{locked.Id}\",\"orderId\":\"{order.Id}\",\"orderNumber\":\"{order.OrderNumber}\",\"paymentDueAt\":\"{order.PaymentDueAt:O}\"}}";

                await _emails.EnqueueAsync(
                  toEmail: toEmail,
                  templateKey: EmailTemplateKeys.WinningBid,
                  payloadJson: payload,
                  dedupeKey: EmailDedupeKeys.WinningBid(locked.Id, toEmail),
                  now: now,
                  ct: ct);
              }
            }
          }
          catch { }
        }

        return (false, null);
      }

      if (locked.ClosingWindowEnd is null)
      {
        await tx.RollbackAsync(ct);
        return (false, "CLOSING_WINDOW_END_MISSING");
      }

      if (locked.ClosingWindowEnd > now)
      {
        await tx.CommitAsync(ct);
        return (false, null);
      }

      var from = locked.Status;

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

        try { await _notifier.NotifyAuctionChangedAsync(locked.Id, ct); } catch { }

        return (true, null);
      }
      catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
      {
        await tx.RollbackAsync(ct);

        try { await _notifier.NotifyAuctionChangedAsync(a.Id, ct); } catch { }

        return (true, null);
      }
    }

    return (false, null);
  }

  private async Task<(bool Changed, string? Error)> TryRelistAuctionAsync(Guid oldAuctionId, DateTimeOffset now, CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

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
      await tx.CommitAsync(ct);
      return (false, null);
    }

    if (old.UpdatedAt > now.Subtract(RelistDelay))
    {
      await tx.CommitAsync(ct);
      return (false, null);
    }

    var exists = await _db.Auctions
      .AsNoTracking()
      .AnyAsync(a => a.RelistOfAuctionId == old.Id, ct);

    if (exists)
    {
      await tx.CommitAsync(ct);
      return (false, null);
    }

    var duration =
      old.StartTime is not null
        ? old.CloseTime - old.StartTime.Value
        : DefaultAuctionDuration;

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
      QuotedShippingCents = old.QuotedShippingCents,
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

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = old.Id,
      UserId = null,
      EventType = "RELIST_TRIGGERED",
      DataJson = $"{{\"newAuctionId\":\"{newAuctionId}\"}}",
      ServerReceivedAt = now
    });

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

      try { await _notifier.NotifyAuctionChangedAsync(old.Id, ct); } catch { }
      try { await _notifier.NotifyAuctionChangedAsync(newAuctionId, ct); } catch { }

      return (true, null);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
    {
      await tx.RollbackAsync(ct);

      try { await _notifier.NotifyAuctionChangedAsync(oldAuctionId, ct); } catch { }

      return (false, null);
    }
  }

  private async Task EnsureUnpaidAuctionOrderExistsAsync(Auction locked, DateTimeOffset now, CancellationToken ct)
  {
    if (locked.CurrentLeaderUserId is null)
      throw new InvalidOperationException("Sold auction must have a winner (CurrentLeaderUserId).");

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

      ShippingMode = "UNSELECTED",
      ShippingAmountCents = 0,

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