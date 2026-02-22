using MineralKingdom.Contracts.Auctions;

namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public sealed class NoopAuctionRealtimePublisher : IAuctionRealtimePublisher
{
  public Task PublishAuctionAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct)
    => Task.CompletedTask;

  public Task PublishSnapshotAsync(AuctionRealtimeSnapshot snapshot, CancellationToken ct)
    => Task.CompletedTask;
}