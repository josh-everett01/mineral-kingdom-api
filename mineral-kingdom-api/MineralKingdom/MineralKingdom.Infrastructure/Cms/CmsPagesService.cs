using Markdig;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Cms;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities.Cms;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Infrastructure.Cms;

public sealed class CmsPagesService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IAuditLogger _audit;

  private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();

  public CmsPagesService(MineralKingdomDbContext db, IAuditLogger audit)
  {
    _db = db;
    _audit = audit;
  }

  public async Task<CmsPublicPageDto?> GetPublishedAsync(string slug, CancellationToken ct)
  {
    slug = (slug ?? "").Trim().ToLowerInvariant();

    var row = await _db.CmsPages
      .AsNoTracking()
      .Where(p => p.Slug == slug && p.IsActive)
      .Select(p => new
      {
        p.Slug,
        p.Title,
        Published = p.Revisions
          .Where(r => r.Status == CmsRevisionStatuses.Published)
          .OrderByDescending(r => r.PublishedAt)
          .Select(r => new { r.ContentHtml, r.ContentMarkdown, r.PublishedAt })
          .FirstOrDefault()
      })
      .SingleOrDefaultAsync(ct);

    if (row is null || row.Published is null || row.Published.PublishedAt is null)
      return null;

    var html = row.Published.ContentHtml;
    if (string.IsNullOrWhiteSpace(html))
      html = Markdown.ToHtml(row.Published.ContentMarkdown ?? "", Pipeline);

    return new CmsPublicPageDto(
      row.Slug,
      row.Title,
      html,
      row.Published.PublishedAt.Value
    );
  }

  public async Task<IReadOnlyList<CmsAdminPageListItem>> AdminListAsync(CancellationToken ct)
  {
    var rows = await _db.CmsPages
      .AsNoTracking()
      .OrderBy(p => p.Slug)
      .Select(p => new
      {
        p.Id,
        p.Slug,
        p.Title,
        p.Category,
        p.IsActive,
        p.UpdatedAt,
        Published = p.Revisions.Where(r => r.Status == CmsRevisionStatuses.Published)
          .OrderByDescending(r => r.PublishedAt)
          .Select(r => new { r.Id, r.PublishedAt })
          .FirstOrDefault()
      })
      .ToListAsync(ct);

    return rows.Select(x => new CmsAdminPageListItem(
      x.Id,
      x.Slug,
      x.Title,
      x.Category,
      x.IsActive,
      x.UpdatedAt,
      x.Published?.PublishedAt,
      x.Published?.Id
    )).ToList();
  }

  public async Task<CmsAdminPageDetailDto?> AdminGetAsync(string slug, CancellationToken ct)
  {
    slug = (slug ?? "").Trim().ToLowerInvariant();

    var page = await _db.CmsPages
      .AsNoTracking()
      .Where(p => p.Slug == slug)
      .Select(p => new
      {
        p.Id,
        p.Slug,
        p.Title,
        p.Category,
        p.IsActive,
        p.CreatedAt,
        p.UpdatedAt,
        Revisions = p.Revisions
          .OrderByDescending(r => r.CreatedAt)
          .Select(r => new CmsRevisionDto(
            r.Id,
            r.Status,
            r.ContentMarkdown,
            r.ContentHtml,
            r.EditorUserId,
            r.PublishedByUserId,
            r.ChangeSummary,
            r.CreatedAt,
            r.PublishedAt,
            r.EffectiveAt
          )).ToList()
      })
      .SingleOrDefaultAsync(ct);

    if (page is null) return null;

    return new CmsAdminPageDetailDto(
      page.Id,
      page.Slug,
      page.Title,
      page.Category,
      page.IsActive,
      page.CreatedAt,
      page.UpdatedAt,
      page.Revisions
    );
  }

  public async Task<(bool Ok, string? Error, UpsertDraftResponse? Response)> UpsertDraftAsync(
    string slug,
    Guid editorUserId,
    string actorRole,
    string markdown,
    string? changeSummary,
    string? ip,
    string? userAgent,
    CancellationToken ct)
  {
    slug = (slug ?? "").Trim().ToLowerInvariant();
    markdown = (markdown ?? "").Trim();

    if (string.IsNullOrWhiteSpace(markdown)) return (false, "CONTENT_REQUIRED", null);
    if (markdown.Length > 20000) return (false, "CONTENT_TOO_LARGE", null);

    var page = await _db.CmsPages.Include(p => p.Revisions).SingleOrDefaultAsync(p => p.Slug == slug, ct);
    if (page is null) return (false, "PAGE_NOT_FOUND", null);
    if (!page.IsActive) return (false, "PAGE_INACTIVE", null);

    // Replace any existing draft by creating a new draft (simpler history)
    var now = DateTimeOffset.UtcNow;

    var draft = new CmsPageRevision
    {
      Id = Guid.NewGuid(),
      PageId = page.Id,
      Status = CmsRevisionStatuses.Draft,
      ContentMarkdown = markdown,
      ContentHtml = null,
      EditorUserId = editorUserId,
      PublishedByUserId = null,
      ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary.Trim(),
      CreatedAt = now,
      PublishedAt = null,
      EffectiveAt = null
    };

    _db.CmsPageRevisions.Add(draft);
    page.UpdatedAt = now;

    // Audit policy edits (and also marketing edits; low-cost to log both)
    await _audit.LogAsync(new AuditEvent(
      ActorUserId: editorUserId,
      ActorRole: actorRole,
      ActionType: "CMS_DRAFT_UPSERT",
      EntityType: "CMS_PAGE",
      EntityId: page.Id,
      Before: null,
      After: new { slug = page.Slug, category = page.Category, draftId = draft.Id, changeSummary = draft.ChangeSummary },
      IpAddress: ip,
      UserAgent: userAgent
    ), ct);

    await _db.SaveChangesAsync(ct);

    return (true, null, new UpsertDraftResponse(draft.Id));
  }

  public async Task<(bool Ok, string? Error)> PublishAsync(
  string slug,
  Guid publisherUserId,
  string actorRole,
  Guid revisionId,
  DateTimeOffset? effectiveAt,
  string? ip,
  string? userAgent,
  CancellationToken ct)
  {
    slug = (slug ?? "").Trim().ToLowerInvariant();

    var page = await _db.CmsPages.SingleOrDefaultAsync(p => p.Slug == slug, ct);
    if (page is null) return (false, "PAGE_NOT_FOUND");
    if (!page.IsActive) return (false, "PAGE_INACTIVE");

    var draft = await _db.CmsPageRevisions.SingleOrDefaultAsync(r => r.Id == revisionId && r.PageId == page.Id, ct);
    if (draft is null) return (false, "REVISION_NOT_FOUND");
    if (draft.Status != CmsRevisionStatuses.Draft) return (false, "REVISION_NOT_DRAFT");

    var now = DateTimeOffset.UtcNow;

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // 1) Archive any prior published revision FIRST (separate SaveChanges to avoid transient unique violation)
    var published = await _db.CmsPageRevisions
      .Where(r => r.PageId == page.Id && r.Status == CmsRevisionStatuses.Published)
      .ToListAsync(ct);

    foreach (var p in published)
      p.Status = CmsRevisionStatuses.Archived;

    if (published.Count > 0)
      await _db.SaveChangesAsync(ct);

    // 2) Publish draft
    draft.Status = CmsRevisionStatuses.Published;
    draft.PublishedByUserId = publisherUserId;
    draft.PublishedAt = now;
    draft.EffectiveAt = effectiveAt;
    draft.ContentHtml = Markdown.ToHtml(draft.ContentMarkdown, Pipeline);

    page.UpdatedAt = now;

    await _audit.LogAsync(new AuditEvent(
      ActorUserId: publisherUserId,
      ActorRole: actorRole,
      ActionType: "CMS_PUBLISH",
      EntityType: "CMS_PAGE",
      EntityId: page.Id,
      Before: published.Count == 0 ? null : new { priorPublishedIds = published.Select(x => x.Id).ToArray() },
      After: new { publishedRevisionId = draft.Id, effectiveAt },
      IpAddress: ip,
      UserAgent: userAgent
    ), ct);

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return (true, null);
  }
}