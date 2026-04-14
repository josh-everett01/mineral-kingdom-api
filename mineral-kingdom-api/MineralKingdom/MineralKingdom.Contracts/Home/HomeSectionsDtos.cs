namespace MineralKingdom.Contracts.Home;

public sealed record HomeSectionsDto(
  HomeSectionDto FeaturedListings,
  HomeSectionDto EndingSoonAuctions,
  HomeSectionDto UpcomingAuctions,
  HomeSectionDto NewArrivals
);

public sealed record HomeSectionDto(
  string Title,
  string BrowseHref,
  int Count,
  List<HomeSectionItemDto> Items
);

public sealed record HomeSectionItemDto(
  Guid ListingId,
  Guid? AuctionId,
  string Title,
  string? PrimaryImageUrl,
  int? PriceCents,
  int? EffectivePriceCents,
  int? CurrentBidCents,
  int? StartingPriceCents,
  DateTimeOffset? EndsAt,
  DateTimeOffset? StartTime,
  string? Status,
  string Href,
  string? DiscountType,
  int? DiscountCents,
  int? DiscountPercentBps
);