namespace MineralKingdom.Contracts.Auctions;

public sealed record CreateAuctionRequest(
  Guid ListingId,
  int StartingPriceCents,
  int? ReservePriceCents,
  DateTimeOffset CloseTime
);
