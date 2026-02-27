using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Cms;
using MineralKingdom.Infrastructure.Cms;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/pages")]
[Authorize(Roles = $"{UserRoles.Owner},{UserRoles.Staff}")]
public sealed class AdminPagesController : ControllerBase
{
  private readonly CmsPagesService _cms;
  private readonly MineralKingdomDbContext _db;

  public AdminPagesController(CmsPagesService cms, MineralKingdomDbContext db)
  {
    _cms = cms;
    _db = db;
  }

  [HttpGet]
  public async Task<IActionResult> List(CancellationToken ct)
    => Ok(await _cms.AdminListAsync(ct));

  [HttpGet("{slug}")]
  public async Task<IActionResult> Get(string slug, CancellationToken ct)
  {
    var dto = await _cms.AdminGetAsync(slug, ct);
    if (dto is null) return NotFound(new { error = "PAGE_NOT_FOUND" });
    return Ok(dto);
  }

  [HttpPost("{slug}/draft")]
  public async Task<IActionResult> UpsertDraft(string slug, [FromBody] UpsertDraftRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var slugNorm = (slug ?? "").Trim().ToLowerInvariant();
    var page = await _db.CmsPages.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slugNorm, ct);
    if (page is null) return NotFound(new { error = "PAGE_NOT_FOUND" });

    // Permissions:
    // - MARKETING: STAFF/OWNER
    // - POLICY: OWNER only
    if (page.Category == CmsPageCategories.Policy && !User.IsInRole(UserRoles.Owner))
      return Forbid();

    var userId = User.GetUserId();
    var role = User.IsInRole(UserRoles.Owner) ? UserRoles.Owner : UserRoles.Staff;
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err, resp) = await _cms.UpsertDraftAsync(
      slugNorm, userId, role, req.ContentMarkdown, req.ChangeSummary, ip, userAgent, ct);

    if (!ok) return BadRequest(new { error = err });
    return Ok(resp);
  }

  [HttpPost("{slug}/publish")]
  public async Task<IActionResult> Publish(string slug, [FromBody] PublishRevisionRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var slugNorm = (slug ?? "").Trim().ToLowerInvariant();
    var page = await _db.CmsPages.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slugNorm, ct);
    if (page is null) return NotFound(new { error = "PAGE_NOT_FOUND" });

    // Permissions:
    // - MARKETING: STAFF/OWNER
    // - POLICY: OWNER only (acceptance criteria)
    if (page.Category == CmsPageCategories.Policy && !User.IsInRole(UserRoles.Owner))
      return Forbid();

    var userId = User.GetUserId();
    var role = User.IsInRole(UserRoles.Owner) ? UserRoles.Owner : UserRoles.Staff;
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _cms.PublishAsync(
      slugNorm, userId, role, req.RevisionId, req.EffectiveAt, ip, userAgent, ct);

    if (!ok) return BadRequest(new { error = err });
    return NoContent();
  }
}