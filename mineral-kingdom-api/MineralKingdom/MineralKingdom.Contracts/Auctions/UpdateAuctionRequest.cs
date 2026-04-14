namespace MineralKingdom.Contracts.Auctions;

public sealed record UpdateAuctionRequest(
  DateTimeOffset? StartTime,
  DateTimeOffset? CloseTime,
  int? StartingPriceCents,
  int? ReservePriceCents,
  int? QuotedShippingCents
);