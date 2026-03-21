namespace MineralKingdom.Contracts.Auctions;

public sealed record AuctionBrowseItemDto(
  Guid Id,
  Guid ListingId,
  string Title,
  string Slug,
  string Href,
  string? PrimaryImageUrl,
  string? LocalityDisplay,
  string? SizeClass,
  bool IsFluorescent,
  int CurrentPriceCents,
  int BidCount,
  DateTimeOffset ClosingTimeUtc,
  string Status
);

public sealed record AuctionBrowseResponseDto(
  IReadOnlyList<AuctionBrowseItemDto> Items,
  int Total,
  DateTimeOffset ServerTimeUtc
);