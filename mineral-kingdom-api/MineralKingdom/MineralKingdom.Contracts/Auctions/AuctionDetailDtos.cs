namespace MineralKingdom.Contracts.Auctions;

public sealed record AuctionDetailMediaDto(
  Guid Id,
  string Url,
  bool IsPrimary,
  int SortOrder
);

public sealed record AuctionDetailDto(
  Guid AuctionId,
  Guid ListingId,
  string Title,
  string? Description,
  string Status,
  int CurrentPriceCents,
  int BidCount,
  bool? ReserveMet,
  DateTimeOffset ClosingTimeUtc,
  int MinimumNextBidCents,
  IReadOnlyList<AuctionDetailMediaDto> Media,
  bool? IsCurrentUserLeading,
  bool? HasCurrentUserBid,
  int? CurrentUserMaxBidCents,
  string? CurrentUserBidState
);