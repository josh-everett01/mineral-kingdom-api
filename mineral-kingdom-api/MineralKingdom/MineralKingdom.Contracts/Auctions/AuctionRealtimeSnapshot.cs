namespace MineralKingdom.Contracts.Auctions;

public sealed record AuctionRealtimeSnapshot(
  Guid AuctionId,
  int CurrentPriceCents,
  int BidCount,
  bool? ReserveMet,
  string Status,
  DateTimeOffset? ClosingWindowEnd,
  int MinimumNextBidCents
);