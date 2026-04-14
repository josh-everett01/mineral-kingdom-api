namespace MineralKingdom.Contracts.Auctions;

public sealed record AuctionBrowseResponseDto(
  List<AuctionBrowseItemDto> Items,
  int Total,
  DateTimeOffset ServerTimeUtc
);

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
  int StartingPriceCents,
  int BidCount,
  DateTimeOffset? StartTimeUtc,
  DateTimeOffset ClosingTimeUtc,
  string Status
);