namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public interface IAuctionRealtimeNotifier
{
  Task NotifyAuctionChangedAsync(Guid auctionId, CancellationToken ct);
}