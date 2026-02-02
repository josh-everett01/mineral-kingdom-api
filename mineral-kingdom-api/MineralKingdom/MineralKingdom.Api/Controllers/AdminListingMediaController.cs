using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/listings/{listingId:guid}/media")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminListingMediaController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminListingMediaController(MineralKingdomDbContext db) => _db = db;

  public sealed record AddListingMediaRequest(
    string MediaType,
    string Url,
    bool? IsPrimary = null,
    int? SortOrder = null,
    string? Caption = null
  );

  public sealed record ListingMediaResponse(Guid Id, string MediaType, string Url, int SortOrder, bool IsPrimary, string? Caption);

  [HttpPost]
  public async Task<ActionResult<ListingMediaResponse>> Add(Guid listingId, [FromBody] AddListingMediaRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var mt = (req.MediaType ?? "").Trim().ToUpperInvariant();
    if (!ListingMediaTypes.IsValid(mt))
      return BadRequest(new { error = "INVALID_MEDIA_TYPE" });

    if (string.IsNullOrWhiteSpace(req.Url))
      return BadRequest(new { error = "URL_REQUIRED" });

    if (mt == ListingMediaTypes.Video && req.IsPrimary == true)
      return BadRequest(new { error = "VIDEO_CANNOT_BE_PRIMARY" });

    var listingExists = await _db.Listings.AsNoTracking().AnyAsync(x => x.Id == listingId, ct);
    if (!listingExists) return NotFound(new { error = "LISTING_NOT_FOUND" });

    var now = DateTimeOffset.UtcNow;

    // Sort order default: append
    var maxSort = await _db.ListingMedia
      .Where(x => x.ListingId == listingId)
      .Select(x => (int?)x.SortOrder)
      .MaxAsync(ct);

    var sort = req.SortOrder ?? ((maxSort ?? -1) + 1);

    // Primary image rules:
    // - videos cannot be primary
    // - if adding first image and no primary exists, auto-set primary if not explicitly false
    bool isPrimary = req.IsPrimary ?? false;

    if (mt == ListingMediaTypes.Image)
    {
      var hasPrimaryImage = await _db.ListingMedia.AnyAsync(
        x => x.ListingId == listingId && x.MediaType == ListingMediaTypes.Image && x.IsPrimary,
        ct
      );

      var hasAnyImage = await _db.ListingMedia.AnyAsync(
        x => x.ListingId == listingId && x.MediaType == ListingMediaTypes.Image,
        ct
      );

      if (!hasAnyImage && !hasPrimaryImage && req.IsPrimary != false)
        isPrimary = true;

      if (isPrimary)
      {
        var existingPrimary = await _db.ListingMedia
          .Where(x => x.ListingId == listingId && x.MediaType == ListingMediaTypes.Image && x.IsPrimary)
          .ToListAsync(ct);

        foreach (var m in existingPrimary) m.IsPrimary = false;
      }
    }

    var row = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      MediaType = mt,
      Url = req.Url.Trim(),
      SortOrder = sort,
      IsPrimary = isPrimary,
      Caption = string.IsNullOrWhiteSpace(req.Caption) ? null : req.Caption.Trim(),
      CreatedAt = now
    };

    _db.ListingMedia.Add(row);
    await _db.SaveChangesAsync(ct);

    return Ok(new ListingMediaResponse(row.Id, row.MediaType, row.Url, row.SortOrder, row.IsPrimary, row.Caption));
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

    // Apply new order
    for (var i = 0; i < req.OrderedMediaIds.Count; i++)
    {
      var id = req.OrderedMediaIds[i];
      var row = media.Single(x => x.Id == id);
      row.SortOrder = i;
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
