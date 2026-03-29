namespace MineralKingdom.Contracts.Auctions;

public sealed record CreateAuctionRequest(
  Guid ListingId,
  int StartingPriceCents,
  int? ReservePriceCents,
  int? QuotedShippingCents,
  DateTimeOffset CloseTime
);