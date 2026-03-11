namespace MineralKingdom.Contracts.Listings;

public sealed record ListingBrowseResponseDto(
  List<ListingBrowseItemDto> Items,
  int Page,
  int PageSize,
  int Total,
  int TotalPages,
  ListingBrowseAvailableFiltersDto AvailableFilters
);

public sealed record ListingBrowseItemDto(
  Guid Id,
  string Slug,
  string Href,
  string Title,
  string? PrimaryImageUrl,
  string? PrimaryMineral,
  string? LocalityDisplay,
  string? SizeClass,
  bool IsFluorescent,
  string ListingType,
  int? PriceCents,
  int? EffectivePriceCents,
  int? CurrentBidCents,
  DateTimeOffset? EndsAt
);

public sealed record ListingBrowseAvailableFiltersDto(
  List<string> MineralTypes,
  List<string> SizeClasses
);