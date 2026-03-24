using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

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

  public sealed record CancelDelayedBidResult(
    bool Ok,
    string? Error
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

    if (auction.Status != AuctionStatuses.Live && auction.Status != AuctionStatuses.Closing)
    {
      await tx.RollbackAsync(ct);
      return await RejectAsyncLocked(auction, userId, maxBidCents, now, "AUCTION_NOT_BIDDABLE", ct);
    }

    if (auction.Status == AuctionStatuses.Closing &&
        auction.ClosingWindowEnd is not null &&
        auction.ClosingWindowEnd.Value <= now)
    {
      await tx.RollbackAsync(ct);
      return await RejectAsyncLocked(auction, userId, maxBidCents, now, "AUCTION_CLOSING_WINDOW_EXPIRED", ct);
    }

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

      var minDelayedAmount = auction.CurrentLeaderUserId is null
        ? auction.StartingPriceCents
        : BidIncrementTable.MinToBeatCents(auction.CurrentPriceCents);

      if (maxBidCents < minDelayedAmount)
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "DELAYED_BID_TOO_LOW", ct);
      }

      await UpsertDelayedBidAsync(auction.Id, userId, maxBidCents, now, ct);

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

      try { await _realtime.PublishAuctionAsync(auction.Id, now, ct); } catch { }

      var (hasReserve, reserveMet) = GetReservePublic(auction);
      return new BidResult(true, null, auction.CurrentPriceCents, auction.CurrentLeaderUserId, hasReserve, reserveMet);
    }

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

      if (auction.CurrentLeaderUserId != userId && maxBidCents < minToBeat)
      {
        await tx.RollbackAsync(ct);
        return await RejectAsyncLocked(auction, userId, maxBidCents, now, "BID_TOO_LOW", ct);
      }
    }

    await UpsertMaxBidAsync(auction.Id, userId, maxBidCents, "IMMEDIATE", now, ct);

    await _db.SaveChangesAsync(ct);

    var oldLeaderUserId = auction.CurrentLeaderUserId;
    var oldCurrentPrice = auction.CurrentPriceCents;

    await RecomputeAndApplyAsync(auction, now, ct);

    if (auction.Status == AuctionStatuses.Closing)
    {
      var proposed = now.Add(ClosingWindowDuration);

      if (auction.ClosingWindowEnd is null || auction.ClosingWindowEnd < proposed)
        auction.ClosingWindowEnd = proposed;

      auction.UpdatedAt = now;
    }

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

    try { await _realtime.PublishAuctionAsync(auction.Id, now, ct); } catch { }

    try
    {
      var newLeaderUserId = auction.CurrentLeaderUserId;

      if (oldLeaderUserId.HasValue &&
          newLeaderUserId.HasValue &&
          oldLeaderUserId.Value != newLeaderUserId.Value)
      {
        var outbidUserId = oldLeaderUserId.Value;

        var toEmail = await _db.Users.AsNoTracking()
          .Where(u => u.Id == outbidUserId)
          .Select(u => u.Email)
          .SingleOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
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
    catch { }

    var (finalHasReserve, finalReserveMet) = GetReservePublic(auction);
    return new BidResult(true, null, auction.CurrentPriceCents, auction.CurrentLeaderUserId, finalHasReserve, finalReserveMet);
  }

  public async Task<CancelDelayedBidResult> CancelDelayedBidAsync(
    Guid auctionId,
    Guid userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var auction = await _db.Auctions
      .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {auctionId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (auction is null)
    {
      await tx.RollbackAsync(ct);
      return new CancelDelayedBidResult(false, "AUCTION_NOT_FOUND");
    }

    var delayed = await _db.AuctionDelayedBids
      .SingleOrDefaultAsync(x => x.AuctionId == auctionId && x.UserId == userId, ct);

    if (delayed is null || string.Equals(delayed.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
    {
      await tx.RollbackAsync(ct);
      return new CancelDelayedBidResult(false, "DELAYED_BID_NOT_FOUND");
    }

    if (string.Equals(delayed.Status, "ACTIVATED", StringComparison.OrdinalIgnoreCase))
    {
      await tx.RollbackAsync(ct);
      return new CancelDelayedBidResult(false, "DELAYED_BID_ALREADY_ACTIVATED");
    }

    delayed.Status = "CANCELLED";
    delayed.CancelledAt = now;
    delayed.UpdatedAt = now;

    _db.AuctionBidEvents.Add(new AuctionBidEvent
    {
      Id = Guid.NewGuid(),
      AuctionId = auctionId,
      UserId = userId,
      EventType = "DELAYED_BID_CANCELLED",
      SubmittedAmountCents = delayed.MaxBidCents,
      Accepted = true,
      ResultingCurrentPriceCents = auction.CurrentPriceCents,
      ResultingLeaderUserId = auction.CurrentLeaderUserId,
      DataJson = null,
      ServerReceivedAt = now
    });

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    try { await _realtime.PublishAuctionAsync(auction.Id, now, ct); } catch { }

    return new CancelDelayedBidResult(true, null);
  }

  /// <summary>
  /// Called by state machine at the moment we enter CLOSING.
  /// Applies eligible delayed bids in UpdatedAt order, converting them into active max bids.
  /// </summary>
  public async Task<(bool Ok, string? Error)> InjectDelayedBidsAtClosingStartAsync(
    Guid auctionId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (_db.Database.CurrentTransaction is null)
      return (false, "MISSING_TRANSACTION");

    var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == auctionId, ct);
    if (auction is null) return (false, "AUCTION_NOT_FOUND");

    if (!string.Equals(auction.Status, AuctionStatuses.Closing, StringComparison.OrdinalIgnoreCase))
      return (false, "AUCTION_NOT_CLOSING");

    var delayed = await _db.AuctionDelayedBids
      .Where(b => b.AuctionId == auction.Id && b.Status == "SCHEDULED")
      .OrderBy(b => b.UpdatedAt)
      .ToListAsync(ct);

    if (delayed.Count == 0)
      return (true, null);

    foreach (var d in delayed)
    {
      var immediateForUser = await _db.AuctionMaxBids
        .SingleOrDefaultAsync(x => x.AuctionId == auction.Id && x.UserId == d.UserId, ct);

      var mootByPrice = auction.CurrentPriceCents >= d.MaxBidCents;
      var mootByImmediateSupersession =
        immediateForUser is not null &&
        immediateForUser.MaxBidCents >= d.MaxBidCents;

      if (mootByPrice || mootByImmediateSupersession)
      {
        d.Status = "MOOT";
        d.MootedAt = now;
        d.UpdatedAt = now;
        continue;
      }

      await UpsertMaxBidAsync(auction.Id, d.UserId, d.MaxBidCents, "IMMEDIATE", d.UpdatedAt, ct);

      d.Status = "ACTIVATED";
      d.ActivatedAt = now;
      d.UpdatedAt = now;
    }

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
    DateTimeOffset receivedAt,
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
        ReceivedAt = receivedAt
      });
      return;
    }

    if (maxBidCents > existing.MaxBidCents)
      existing.MaxBidCents = maxBidCents;

    existing.BidType = bidType;
  }

  private async Task UpsertDelayedBidAsync(
    Guid auctionId,
    Guid userId,
    int maxBidCents,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var existing = await _db.AuctionDelayedBids.SingleOrDefaultAsync(
      x => x.AuctionId == auctionId && x.UserId == userId, ct);

    if (existing is null)
    {
      _db.AuctionDelayedBids.Add(new AuctionDelayedBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = maxBidCents,
        Status = "SCHEDULED",
        CreatedAt = now,
        UpdatedAt = now
      });

      return;
    }

    existing.MaxBidCents = maxBidCents;
    existing.Status = "SCHEDULED";
    existing.CancelledAt = null;
    existing.MootedAt = null;
    existing.ActivatedAt = null;
    existing.UpdatedAt = now;
  }

  private async Task RecomputeAndApplyAsync(Auction auction, DateTimeOffset now, CancellationToken ct)
  {
    var bids = await _db.AuctionMaxBids
      .Where(b => b.AuctionId == auction.Id && b.BidType == "IMMEDIATE")
      .ToListAsync(ct);

    if (bids.Count == 0)
    {
      auction.CurrentLeaderUserId = null;
      auction.CurrentLeaderMaxCents = null;
      auction.CurrentPriceCents = auction.StartingPriceCents;
      auction.BidCount = 0;
      auction.ReserveMet = false;
      auction.UpdatedAt = now;
      return;
    }

    var ordered = bids
      .OrderByDescending(b => b.MaxBidCents)
      .ThenBy(b => b.ReceivedAt)
      .ThenBy(b => b.UserId)
      .ToList();

    var winner = ordered[0];
    var winnerMax = winner.MaxBidCents;
    var secondMax = ordered.Count > 1 ? ordered[1].MaxBidCents : auction.StartingPriceCents;

    var increment = BidIncrementTable.GetIncrementCents(secondMax);
    var computed = checked(secondMax + increment);
    var newPrice = Math.Min(computed, winnerMax);

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
    auction.BidCount = bids.Count;
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

    try { await _db.SaveChangesAsync(ct); } catch { }

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

    if (auction.ClosingWindowEnd is not null)
    {
      auction.ClosingWindowEnd = null;
      auction.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
    }

    if (auction.CloseTime > now)
      return (true, null);

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