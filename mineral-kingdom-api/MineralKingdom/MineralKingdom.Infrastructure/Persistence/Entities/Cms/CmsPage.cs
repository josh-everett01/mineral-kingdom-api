namespace MineralKingdom.Infrastructure.Persistence.Entities.Cms;

public sealed class CmsPage
{
  public Guid Id { get; set; }

  public string Slug { get; set; } = default!;
  public string Title { get; set; } = default!;
  public string Category { get; set; } = default!; // MARKETING|POLICY

  public bool IsActive { get; set; } = true;

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<CmsPageRevision> Revisions { get; set; } = new();
}