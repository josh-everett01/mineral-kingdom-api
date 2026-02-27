namespace MineralKingdom.Contracts.Cms;

public static class CmsPageCategories
{
  public const string Marketing = "MARKETING";
  public const string Policy = "POLICY";
}

public static class CmsRevisionStatuses
{
  public const string Draft = "DRAFT";
  public const string Published = "PUBLISHED";
  public const string Archived = "ARCHIVED";
}

public sealed record CmsPublicPageDto(
  string Slug,
  string Title,
  string ContentHtml,
  DateTimeOffset PublishedAt
);

public sealed record CmsRevisionDto(
  Guid Id,
  string Status,
  string ContentMarkdown,
  string? ContentHtml,
  Guid EditorUserId,
  Guid? PublishedByUserId,
  string? ChangeSummary,
  DateTimeOffset CreatedAt,
  DateTimeOffset? PublishedAt,
  DateTimeOffset? EffectiveAt
);

public sealed record CmsAdminPageListItem(
  Guid Id,
  string Slug,
  string Title,
  string Category,
  bool IsActive,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? PublishedAt,
  Guid? PublishedRevisionId
);

public sealed record CmsAdminPageDetailDto(
  Guid Id,
  string Slug,
  string Title,
  string Category,
  bool IsActive,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  IReadOnlyList<CmsRevisionDto> Revisions
);

public sealed record UpsertDraftRequest(string ContentMarkdown, string? ChangeSummary);
public sealed record UpsertDraftResponse(Guid RevisionId);

public sealed record PublishRevisionRequest(Guid RevisionId, DateTimeOffset? EffectiveAt);