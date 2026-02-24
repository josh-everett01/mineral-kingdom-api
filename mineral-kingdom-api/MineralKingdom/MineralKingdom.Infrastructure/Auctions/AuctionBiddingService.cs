using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MineralKingdom.Infrastructure.Auctions;

public sealed class AuctionBiddingService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IAuctionRealtimePublisher _realtime;
  private readonly EmailOutboxService _emails;
  private readonly UserNotificationPreferencesService _prefs;

  public AuctionBiddingService(
  MineralKingdomDbContext db,
  IAuctionRealtimePublisher realtime,
  EmailOutboxService emails,
  UserNotificationPreferencesService prefs)
  {
    _db = db;
    _realtime = realtime;
    _emails = emails;
    _prefs = prefs;
  }

  private static readonly TimeSpan ClosingWindowDuration = TimeSpan.FromMinutes(10);

  public sealed record BidResult(
    bool Ok,
    string? Error,
    int? CurrentPriceCents,
    Guid? LeaderUserId,
    bool HasReserve,
    bool? ReserveMet
  );

  public async Task<BidResult> PlaceBidAsync(
    Guid auctionId,
    Guid userId,
    int maxBidCents,
    string mode,
    DateTimeOffset now,
    CancellationToken ct)
  {
    mode = (mode ?? "IMMEDIATE").Trim().ToUpperInvariant();
    if (mode != "IMMEDIATE" && mode != "DELAYED")
      return await RejectAsync(auctionId, userId, maxBidCents, now, "INVALID_MODE", ct);

    if (maxBidCents <= 0)
      return await RejectAsync(auctionId, userId, maxBidCents, now, "INVALID_BID", ct);

    if (!BidIncrementTable.IsWholeDollar(maxBidCents))
      return await RejectAsync(auctionId, userId, maxBidCents, now, "NOT_WHOLE_DOLLARS", ct);

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // Concurrency safety: lock auction row
    var auction = await _db.Auctions
      .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {auctionId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (auction is null)
    {
      await tx.RollbackAsync(ct);
      return new BidResult(false, "AUCTION_NOT_FOUND", null, null, false, null);
    }

    var (okClose, errClose) = await EnsureClosingIfPastScheduledCloseAsync(auction, now, ct);
    if (!okClose)
    {
      await tx.RollbackAsync(ct);
      return new BidResult(
        Ok: false,
        Error: errClose ?? "FAILED_TO_ENTER_CLOSING",
        CurrentPriceCents: null,
        LeaderUserId: null,
        HasReserve: false,
        ReserveMet: null
      );
    }

    // Validate status
    if (auction.Status != AuctionStatuses.Live && auction.Status != AuctionStatuses.Closing)
    {
      await tx.RollbackAsync(ct);
      return await RejectAsyncLocked(auction, userId, maxBidCents, now, "AUCTION_NOT_BIDDABLE", ct);
    }

    // If CLOSING window already expired, reject (worker should finalize imminently)
    if (auction.Status == AuctionStatuses.Closing &&
        auction.ClosingWindowEnd is not null &&
        auction.ClosingWindowEnd.Value <= now)
    {
      await tx.RollbackAsync(ct);
      return await RejectAsyncLocked(auction, userId, maxBidCents, now, "AUCTION_CLOSING_WINDOW_EXPIRED", ct);
    }

    // Delayed registration: must be LIVE and at least 3h before scheduled close
    if (mode == "DELAYED")
    {
      if (auction.Status != AuctionStatuses.Live)
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "DELAYED_ONLY_WHILE_LIVE", ct);
      }

      if (now > auction.CloseTime.AddHours(-3))
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "DELAYED_TOO_LATE", ct);
      }

      // Store delayed max bid (private)
      await UpsertMaxBidAsync(auction.Id, userId, maxBidCents, "DELAYED", now, ct);

      _db.AuctionBidEvents.Add(new AuctionBidEvent
      {
        Id = Guid.NewGuid(),
        AuctionId = auction.Id,
        UserId = userId,
        EventType = "DELAYED_BID_REGISTERED",
        SubmittedAmountCents = maxBidCents,
        Accepted = true,
        ResultingCurrentPriceCents = auction.CurrentPriceCents,
        ResultingLeaderUserId = auction.CurrentLeaderUserId,
        DataJson = null,
        ServerReceivedAt = now
      });

      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);

      // ✅ Publish AFTER commit
      try { await _realtime.PublishAuctionAsync(auction.Id, now, ct); } catch { /* best-effort */ }

      var (hasReserve, reserveMet) = GetReservePublic(auction);
      return new BidResult(true, null, auction.CurrentPriceCents, auction.CurrentLeaderUserId, hasReserve, reserveMet);
    }

    // If no leader yet, the first bid just needs to meet starting price.
    // If there is a leader (competing), enforce increment threshold.
    if (auction.CurrentLeaderUserId is null)
    {
      if (maxBidCents < auction.StartingPriceCents)
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "BID_TOO_LOW", ct);
      }
    }
    else
    {
      var minToBeat = BidIncrementTable.MinToBeatCents(auction.CurrentPriceCents);

      // Allow leader to "increase max" without needing to beat current price + increment
      if (auction.CurrentLeaderUserId != userId && maxBidCents < minToBeat)
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "BID_TOO_LOW", ct);
      }
    }

    // Upsert user max
    await UpsertMaxBidAsync(auction.Id, userId, maxBidCents, "IMMEDIATE", now, ct);

    await _db.SaveChangesAsync(ct); // ensure recompute query can see max bid

    var oldLeaderUserId = auction.CurrentLeaderUserId;
    var oldCurrentPrice = auction.CurrentPriceCents;
    // Recompute derived fields based on all IMMEDIATE max bids (and any previously injected delayed bids)
    await RecomputeAndApplyAsync(auction, now, ct);

    // If we're in CLOSING, reset closing window end (quiet period extension)
    if (auction.Status == AuctionStatuses.Closing)
    {
      var proposed = now.Add(ClosingWindowDuration);

      if (auction.ClosingWindowEnd is null || auction.ClosingWindowEnd < proposed)
        auction.ClosingWindowEnd = proposed;

      auction.UpdatedAt = now;
    }

    // Log accepted attempt
    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = userId,
      EventType = "BID_ACCEPTED",
      SubmittedAmountCents = maxBidCents,
      Accepted = true,
      ResultingCurrentPriceCents = auction.CurrentPriceCents,
      ResultingLeaderUserId = auction.CurrentLeaderUserId,
      DataJson = null,
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    // ✅ Publish AFTER commit
    try { await _realtime.PublishAuctionAsync(auction.Id, now, ct); } catch { /* best-effort */ }
    // S6-4 Optional email: OUTBID (toggleable by user prefs)
    // Trigger: leader changes from oldLeaderUserId -> new leader
    try
    {
      var newLeaderUserId = auction.CurrentLeaderUserId;

      if (oldLeaderUserId.HasValue &&
          newLeaderUserId.HasValue &&
          oldLeaderUserId.Value != newLeaderUserId.Value)
      {
        // Only notify the OUTBID user (old leader), not the new leader
        var outbidUserId = oldLeaderUserId.Value;

        // Load email for the outbid user
        var toEmail = await _db.Users.AsNoTracking()
          .Where(u => u.Id == outbidUserId)
          .Select(u => u.Email)
          .SingleOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
          // Respect optional preference toggle
          var prefs = await _prefs.GetOrCreateAsync(outbidUserId, now, ct);
          if (UserNotificationPreferencesService.IsEnabled(prefs, OptionalEmailKeys.Outbid))
          {
            var payload =
              $"{{\"auctionId\":\"{auction.Id}\",\"outbidUserId\":\"{outbidUserId}\",\"newLeaderUserId\":\"{newLeaderUserId}\",\"oldPriceCents\":{oldCurrentPrice},\"newPriceCents\":{auction.CurrentPriceCents}}}";

            await _emails.EnqueueAsync(
              toEmail: toEmail,
              templateKey: EmailTemplateKeys.Outbid,
              payloadJson: payload,
              dedupeKey: EmailDedupeKeys.Outbid(auction.Id, outbidUserId, auction.CurrentPriceCents, toEmail),
              now: now,
              ct: ct);
          }
        }
      }
    }
    catch { /* best-effort */ }
    var (finalHasReserve, finalReserveMet) = GetReservePublic(auction);
    return new BidResult(true, null, auction.CurrentPriceCents, auction.CurrentLeaderUserId, finalHasReserve, finalReserveMet);
  }

  /// <summary>
  /// Called by state machine at the moment we enter CLOSING.
  /// Applies eligible delayed bids in ReceivedAt order, converting them into active max bids.
  /// </summary>
  public async Task<(bool Ok, string? Error)> InjectDelayedBidsAtClosingStartAsync(
    Guid auctionId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (_db.Database.CurrentTransaction is null)
      return (false, "MISSING_TRANSACTION");

    // IMPORTANT: caller should already hold a transaction + auction row lock.
    var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == auctionId, ct);
    if (auction is null) return (false, "AUCTION_NOT_FOUND");

    if (!string.Equals(auction.Status, AuctionStatuses.Closing, StringComparison.OrdinalIgnoreCase))
      return (false, "AUCTION_NOT_CLOSING");

    var delayed = await _db.AuctionMaxBids
      .Where(b => b.AuctionId == auction.Id && b.BidType == "DELAYED")
      .OrderBy(b => b.ReceivedAt)
      .ToListAsync(ct);

    if (delayed.Count == 0)
      return (true, null);

    foreach (var d in delayed)
      d.BidType = "IMMEDIATE";

    await _db.SaveChangesAsync(ct);

    await RecomputeAndApplyAsync(auction, now, ct);

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = null,
      EventType = "DELAYED_BIDS_INJECTED",
      Accepted = true,
      ResultingCurrentPriceCents = auction.CurrentPriceCents,
      ResultingLeaderUserId = auction.CurrentLeaderUserId,
      DataJson = null,
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);

    return (true, null);
  }

  private async Task UpsertMaxBidAsync(
    Guid auctionId,
    Guid userId,
    int maxBidCents,
    string bidType,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var existing = await _db.AuctionMaxBids.SingleOrDefaultAsync(
      x => x.AuctionId == auctionId && x.UserId == userId, ct);

    if (existing is null)
    {
      _db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = maxBidCents,
        BidType = bidType,
        ReceivedAt = now // first receipt time becomes tie-breaker
      });
      return;
    }

    // Keep earliest ReceivedAt to preserve tie-breaker. Only increase max if higher.
    if (maxBidCents > existing.MaxBidCents)
      existing.MaxBidCents = maxBidCents;

    // For simplicity in S5-2: last mode wins (but we do not change ReceivedAt).
    existing.BidType = bidType;
  }

  private async Task RecomputeAndApplyAsync(Auction auction, DateTimeOffset now, CancellationToken ct)
  {
    // Consider only IMMEDIATE for price formation
    var bids = await _db.AuctionMaxBids
      .Where(b => b.AuctionId == auction.Id && b.BidType == "IMMEDIATE")
      .ToListAsync(ct);

    if (bids.Count == 0)
    {
      auction.CurrentLeaderUserId = null;
      auction.CurrentLeaderMaxCents = null;
      auction.CurrentPriceCents = auction.StartingPriceCents;
      auction.BidCount = 0;

      // Reserve semantics:
      // - keep stored false, but API will expose HasReserve + ReserveMet? to prevent misread
      auction.ReserveMet = false;

      auction.UpdatedAt = now;
      return;
    }

    // Highest max wins; tie -> earliest ReceivedAt wins
    var ordered = bids
      .OrderByDescending(b => b.MaxBidCents)
      .ThenBy(b => b.ReceivedAt)
      .ThenBy(b => b.UserId) // deterministic tie-break if same timestamp
      .ToList();

    var winner = ordered[0];
    var winnerMax = winner.MaxBidCents;

    var secondMax = ordered.Count > 1 ? ordered[1].MaxBidCents : auction.StartingPriceCents;

    var increment = BidIncrementTable.GetIncrementCents(secondMax);
    var computed = checked(secondMax + increment);

    // Current price is runner-up + increment, but never above winner max.
    var newPrice = Math.Min(computed, winnerMax);

    // Reserve: only meaningful if ReservePriceCents exists
    bool reserveMet;
    if (auction.ReservePriceCents is null)
    {
      reserveMet = false;
    }
    else
    {
      reserveMet = winnerMax >= auction.ReservePriceCents.Value;
      if (reserveMet)
        newPrice = Math.Max(newPrice, auction.ReservePriceCents.Value);
    }

    auction.CurrentLeaderUserId = winner.UserId;
    auction.CurrentLeaderMaxCents = winnerMax;
    auction.CurrentPriceCents = newPrice;
    auction.BidCount = bids.Count; // active max bidders
    auction.ReserveMet = reserveMet;
    auction.UpdatedAt = now;
  }

  private async Task<BidResult> RejectAsync(
    Guid auctionId,
    Guid userId,
    int maxBidCents,
    DateTimeOffset now,
    string error,
    CancellationToken ct)
  {
    // best-effort log without requiring auction lock
    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auctionId,
      UserId = userId,
      EventType = "BID_REJECTED",
      SubmittedAmountCents = maxBidCents,
      Accepted = false,
      ServerReceivedAt = now
    });

    try { await _db.SaveChangesAsync(ct); } catch { /* ignore */ }

    return new BidResult(false, error, null, null, false, null);
  }

  private async Task<BidResult> RejectAsyncLocked(
    Auction auction,
    Guid userId,
    int maxBidCents,
    DateTimeOffset now,
    string error,
    CancellationToken ct)
  {
    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = userId,
      EventType = "BID_REJECTED",
      SubmittedAmountCents = maxBidCents,
      Accepted = false,
      ResultingCurrentPriceCents = auction.CurrentPriceCents,
      ResultingLeaderUserId = auction.CurrentLeaderUserId,
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);

    var (hasReserve, reserveMet) = GetReservePublic(auction);
    return new BidResult(false, error, auction.CurrentPriceCents, auction.CurrentLeaderUserId, hasReserve, reserveMet);
  }

  private async Task<(bool Ok, string? Error)> EnsureClosingIfPastScheduledCloseAsync(
    Auction auction,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (!string.Equals(auction.Status, AuctionStatuses.Live, StringComparison.OrdinalIgnoreCase))
      return (true, null);

    // Defensive: LIVE should not carry a closing window
    if (auction.ClosingWindowEnd is not null)
    {
      auction.ClosingWindowEnd = null;
      auction.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
    }

    if (auction.CloseTime > now)
      return (true, null);

    // Transition LIVE -> CLOSING authoritatively on server time
    auction.Status = AuctionStatuses.Closing;

    var proposed = now.Add(ClosingWindowDuration);
    if (auction.ClosingWindowEnd is null || auction.ClosingWindowEnd < proposed)
      auction.ClosingWindowEnd = proposed;

    auction.UpdatedAt = now;

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auction.Id,
      UserId = null,
      EventType = "STATUS_CHANGED",
      DataJson = "{\"from\":\"LIVE\",\"to\":\"CLOSING\",\"reason\":\"CLOSE_TIME_REACHED_IN_BID\"}",
      ServerReceivedAt = now
    });

    // Persist the status transition so injection/recompute can read consistent state
    await _db.SaveChangesAsync(ct);

    var (ok, err) = await InjectDelayedBidsAtClosingStartAsync(auction.Id, now, ct);
    if (!ok) return (false, err);

    return (true, null);
  }

  private static (bool HasReserve, bool? ReserveMet) GetReservePublic(Auction auction)
  {
    var hasReserve = auction.ReservePriceCents is not null;
    var reserveMet = hasReserve ? auction.ReserveMet : (bool?)null;
    return (hasReserve, reserveMet);
  }
}