using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public sealed class AuctionRealtimePublisher : IAuctionRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionRealtimeHub _hub;

  public AuctionRealtimePublisher(MineralKingdomDbContext db, AuctionRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishAuctionAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
  {
    var a = await _db.Auctions
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == auctionId, ct);

    if (a is null) return;

    var hasReserve = a.ReservePriceCents is not null;
    var reserveMet = hasReserve ? a.ReserveMet : (bool?)null;

    var minNext = a.BidCount <= 0
      ? a.StartingPriceCents
      : BidIncrementTable.MinToBeatCents(a.CurrentPriceCents);

    _hub.Publish(auctionId, new AuctionRealtimeSnapshot(
      AuctionId: a.Id,
      CurrentPriceCents: a.CurrentPriceCents,
      BidCount: a.BidCount,
      ReserveMet: reserveMet,
      Status: a.Status,
      ClosingWindowEnd: a.ClosingWindowEnd,
      MinimumNextBidCents: minNext
    ));
  }
}