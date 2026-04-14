namespace MineralKingdom.Contracts.Auctions;

public sealed record AdminAuctionDetailDto(
  Guid AuctionId,
  Guid ListingId,
  string? ListingTitle,
  string Status,
  int StartingPriceCents,
  int CurrentPriceCents,
  int? ReservePriceCents,
  bool HasReserve,
  bool? ReserveMet,
  int BidCount,
  DateTimeOffset? StartTime,
  DateTimeOffset CloseTime,
  DateTimeOffset? ClosingWindowEnd,
  int? QuotedShippingCents,
  Guid? RelistOfAuctionId,
  Guid? ReplacementAuctionId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset ServerTimeUtc,
  bool IsCloseDue,
  int SecondsUntilCloseDue,
  bool IsClosingWindowDue,
  int? SecondsUntilClosingWindowDue,
  bool IsRelistDue,
  int SecondsUntilRelistDue
);