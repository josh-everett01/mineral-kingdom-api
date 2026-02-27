namespace MineralKingdom.Infrastructure.Persistence.Entities.Cms;

public sealed class CmsPageRevision
{
  public Guid Id { get; set; }

  public Guid PageId { get; set; }
  public CmsPage Page { get; set; } = default!;

  public string Status { get; set; } = default!; // DRAFT|PUBLISHED|ARCHIVED

  public string ContentMarkdown { get; set; } = default!;
  public string? ContentHtml { get; set; }

  public Guid EditorUserId { get; set; }
  public Guid? PublishedByUserId { get; set; }

  public string? ChangeSummary { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
  public DateTimeOffset? EffectiveAt { get; set; }
}