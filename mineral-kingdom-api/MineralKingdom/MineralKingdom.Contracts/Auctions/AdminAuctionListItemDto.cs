namespace MineralKingdom.Contracts.Auctions;

public sealed record AdminAuctionListItemDto(
  Guid Id,
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
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt
);