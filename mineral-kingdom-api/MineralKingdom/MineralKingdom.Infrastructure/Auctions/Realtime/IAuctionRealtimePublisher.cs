namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public interface IAuctionRealtimePublisher
{
  Task PublishAuctionAsync(Guid auctionId, DateTimeOffset now, CancellationToken ct);
}