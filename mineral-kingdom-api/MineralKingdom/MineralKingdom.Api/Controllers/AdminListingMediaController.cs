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
[Route("api/admin/listings/{listingId:guid}/media")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminListingMediaController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly MediaUploadService _uploads;

  public AdminListingMediaController(MineralKingdomDbContext db, MediaUploadService uploads)
  {
    _db = db;
    _uploads = uploads;
  }

  public sealed record InitiateUploadRequest(
    string MediaType,
    string FileName,
    string ContentType,
    long ContentLengthBytes,
    bool? IsPrimary = null,
    int? SortOrder = null,
    string? Caption = null
  );

  public sealed record InitiateUploadResponse(
    Guid MediaId,
    string StorageKey,
    string UploadUrl,
    Dictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt,
    string PublicUrl
  );

  [HttpPost("initiate")]
  public async Task<ActionResult<InitiateUploadResponse>> Initiate(Guid listingId, [FromBody] InitiateUploadRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var (ok, error, result) = await _uploads.InitiateAsync(
      new MediaUploadService.InitiateRequest(
        ListingId: listingId,
        MediaType: req.MediaType,
        FileName: req.FileName,
        ContentType: req.ContentType,
        ContentLengthBytes: req.ContentLengthBytes,
        IsPrimary: req.IsPrimary,
        SortOrder: req.SortOrder,
        Caption: req.Caption
      ),
      ct
    );

    if (!ok)
      return BadRequest(new { error });

    return Ok(new InitiateUploadResponse(
      result!.MediaId,
      result.StorageKey,
      result.UploadUrl,
      result.RequiredHeaders,
      result.ExpiresAt,
      result.PublicUrl
    ));
  }

  public sealed record ReorderMediaRequest(List<Guid> OrderedMediaIds);

  [HttpPatch("reorder")]
  public async Task<IActionResult> Reorder(Guid listingId, [FromBody] ReorderMediaRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    if (req.OrderedMediaIds is null || req.OrderedMediaIds.Count == 0)
      return BadRequest(new { error = "ORDER_REQUIRED" });

    var media = await _db.ListingMedia
      .Where(x => x.ListingId == listingId)
      .ToListAsync(ct);

    var set = media.Select(x => x.Id).ToHashSet();
    if (req.OrderedMediaIds.Any(id => !set.Contains(id)))
      return BadRequest(new { error = "MEDIA_NOT_IN_LISTING" });

    for (var i = 0; i < req.OrderedMediaIds.Count; i++)
    {
      var id = req.OrderedMediaIds[i];
      var row = media.Single(x => x.Id == id);
      row.SortOrder = i;
      row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  private bool TryGetActorId(out Guid actorId)
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    return Guid.TryParse(raw, out actorId);
  }
}
