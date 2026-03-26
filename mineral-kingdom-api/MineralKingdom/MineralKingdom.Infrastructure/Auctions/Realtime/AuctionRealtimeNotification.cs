namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public sealed record AuctionRealtimeNotification(Guid AuctionId)
{
  public const string ChannelName = "auction_realtime";
}