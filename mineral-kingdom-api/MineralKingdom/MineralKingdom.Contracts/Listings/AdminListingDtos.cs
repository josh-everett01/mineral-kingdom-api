namespace MineralKingdom.Contracts.Listings;

public sealed record AdminListingPublishChecklistDto(
  bool CanPublish,
  List<string> Missing
);

public sealed record AdminListingMediaSummaryDto(
  int ReadyImageCount,
  int PrimaryReadyImageCount,
  bool HasPrimaryVideoViolation
);

public sealed record AdminListingListItemDto(
  Guid Id,
  string? Title,
  string Status,
  Guid? PrimaryMineralId,
  string? PrimaryMineralName,
  string? LocalityDisplay,
  int QuantityAvailable,
  int QuantityTotal,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? PublishedAt,
  DateTimeOffset? ArchivedAt,
  AdminListingPublishChecklistDto PublishChecklist
);

public sealed record AdminListingDetailDto(
  Guid Id,
  string Status,
  string? Title,
  string? Description,
  Guid? PrimaryMineralId,
  string? PrimaryMineralName,
  string? LocalityDisplay,
  string? CountryCode,
  string? AdminArea1,
  string? AdminArea2,
  string? MineName,
  decimal? LengthCm,
  decimal? WidthCm,
  decimal? HeightCm,
  int? WeightGrams,
  string? SizeClass,
  bool IsFluorescent,
  string? FluorescenceNotes,
  string? ConditionNotes,
  bool IsLot,
  int QuantityTotal,
  int QuantityAvailable,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? PublishedAt,
  DateTimeOffset? ArchivedAt,
  AdminListingMediaSummaryDto MediaSummary,
  AdminListingPublishChecklistDto PublishChecklist
);

public sealed record AdminMineralLookupItemDto(
  Guid Id,
  string Name
);