using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Media;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/media")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminMediaController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly MediaUploadService _uploads;

  public AdminMediaController(MineralKingdomDbContext db, MediaUploadService uploads)
  {
    _db = db;
    _uploads = uploads;
  }

  [HttpPost("{mediaId:guid}/complete")]
  public async Task<IActionResult> Complete(Guid mediaId, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var (ok, error) = await _uploads.CompleteAsync(mediaId, ct);
    if (!ok)
      return BadRequest(new { error });

    return NoContent();
  }

  private bool TryGetActorId(out Guid actorId)
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    return Guid.TryParse(raw, out actorId);
  }

  [HttpDelete("/api/admin/media/{mediaId:guid}")]
  public async Task<IActionResult> Delete(Guid mediaId, CancellationToken ct)
  {
    var (ok, err) = await _uploads.DeleteAsync(mediaId, ct);
    if (ok) return NoContent();

    if (err == "MEDIA_DELETE_BLOCKED_AUCTION_ACTIVE")
      return Conflict(new { error = err });

    if (err == "MEDIA_NOT_FOUND")
      return NotFound(new { error = err });

    return BadRequest(new { error = err });
  }

  [HttpPost("{mediaId:guid}/make-primary")]
  public async Task<IActionResult> MakePrimary(Guid mediaId, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists)
      return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var media = await _db.ListingMedia
      .SingleOrDefaultAsync(x => x.Id == mediaId && x.DeletedAt == null, ct);

    if (media is null)
      return NotFound(new { error = "MEDIA_NOT_FOUND" });

    if (!string.Equals(media.MediaType, ListingMediaTypes.Image, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "MEDIA_PRIMARY_ONLY_IMAGE" });

    if (!string.Equals(media.Status, ListingMediaStatuses.Ready, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "MEDIA_PRIMARY_ONLY_READY" });

    var siblings = await _db.ListingMedia
      .Where(x =>
        x.ListingId == media.ListingId &&
        x.DeletedAt == null &&
        x.MediaType == ListingMediaTypes.Image)
      .ToListAsync(ct);

    foreach (var sibling in siblings)
    {
      sibling.IsPrimary = sibling.Id == media.Id;
      sibling.UpdatedAt = DateTimeOffset.UtcNow;
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }
}
