using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/listings")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminListingsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly IAuditLogger _audit;

  public AdminListingsController(MineralKingdomDbContext db, IAuditLogger audit)
  {
    _db = db;
    _audit = audit;
  }

  public sealed record CreateListingRequest(
    string? Title = null,
    string? Description = null
  );

  public sealed record ListingIdResponse(Guid Id);

  [HttpPost]
  public async Task<ActionResult<ListingIdResponse>> Create([FromBody] CreateListingRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    // Tighten security: ensure actor exists
    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim(),
      Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Listings.Add(listing);
    await _db.SaveChangesAsync(ct);

    return Ok(new ListingIdResponse(listing.Id));
  }

  public sealed record UpdateListingRequest(
    string? Title = null,
    string? Description = null,
    Guid? PrimaryMineralId = null,

    string? LocalityDisplay = null,
    string? CountryCode = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? MineName = null,

    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    int? WeightGrams = null,

    string? SizeClass = null,
    bool? IsFluorescent = null,
    string? FluorescenceNotes = null,
    string? ConditionNotes = null,

    bool? IsLot = null,
    int? QuantityTotal = null,
    int? QuantityAvailable = null
  );

  [HttpPatch("{id:guid}")]
  public async Task<IActionResult> Update(Guid id, [FromBody] UpdateListingRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var listing = await _db.Listings.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (listing is null) return NotFound(new { error = "LISTING_NOT_FOUND" });

    if (string.Equals(listing.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "LISTING_ARCHIVED" });

    // Apply changes only when provided
    if (req.Title is not null) listing.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
    if (req.Description is not null) listing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();

    if (req.PrimaryMineralId.HasValue) listing.PrimaryMineralId = req.PrimaryMineralId;

    if (req.LocalityDisplay is not null) listing.LocalityDisplay = string.IsNullOrWhiteSpace(req.LocalityDisplay) ? null : req.LocalityDisplay.Trim();
    if (req.CountryCode is not null) listing.CountryCode = string.IsNullOrWhiteSpace(req.CountryCode) ? null : req.CountryCode.Trim().ToUpperInvariant();
    if (req.AdminArea1 is not null) listing.AdminArea1 = string.IsNullOrWhiteSpace(req.AdminArea1) ? null : req.AdminArea1.Trim();
    if (req.AdminArea2 is not null) listing.AdminArea2 = string.IsNullOrWhiteSpace(req.AdminArea2) ? null : req.AdminArea2.Trim();
    if (req.MineName is not null) listing.MineName = string.IsNullOrWhiteSpace(req.MineName) ? null : req.MineName.Trim();

    if (req.LengthCm.HasValue) listing.LengthCm = req.LengthCm;
    if (req.WidthCm.HasValue) listing.WidthCm = req.WidthCm;
    if (req.HeightCm.HasValue) listing.HeightCm = req.HeightCm;
    if (req.WeightGrams.HasValue) listing.WeightGrams = req.WeightGrams;

    if (req.SizeClass is not null) listing.SizeClass = string.IsNullOrWhiteSpace(req.SizeClass) ? null : req.SizeClass.Trim().ToUpperInvariant();
    if (req.IsFluorescent.HasValue) listing.IsFluorescent = req.IsFluorescent.Value;
    if (req.FluorescenceNotes is not null) listing.FluorescenceNotes = string.IsNullOrWhiteSpace(req.FluorescenceNotes) ? null : req.FluorescenceNotes.Trim();
    if (req.ConditionNotes is not null) listing.ConditionNotes = string.IsNullOrWhiteSpace(req.ConditionNotes) ? null : req.ConditionNotes.Trim();

    if (req.IsLot.HasValue) listing.IsLot = req.IsLot.Value;
    if (req.QuantityTotal.HasValue) listing.QuantityTotal = req.QuantityTotal.Value;
    if (req.QuantityAvailable.HasValue) listing.QuantityAvailable = req.QuantityAvailable.Value;

    listing.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct);

    return NoContent();
  }

  [HttpPost("{id:guid}/publish")]
  public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var listing = await _db.Listings.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (listing is null) return NotFound(new { error = "LISTING_NOT_FOUND" });

    if (string.Equals(listing.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase))
      return Conflict(new { error = "LISTING_ARCHIVED" });

    if (string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return NoContent(); // idempotent

    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(listing.Title)) missing.Add("TITLE");
    if (string.IsNullOrWhiteSpace(listing.Description)) missing.Add("DESCRIPTION");
    if (listing.PrimaryMineralId is null) missing.Add("PRIMARY_MINERAL");
    if (string.IsNullOrWhiteSpace(listing.CountryCode)) missing.Add("COUNTRY");
    if (!IsPositive(listing.LengthCm)) missing.Add("LENGTH_CM");
    if (!IsPositive(listing.WidthCm)) missing.Add("WIDTH_CM");
    if (!IsPositive(listing.HeightCm)) missing.Add("HEIGHT_CM");

    if (listing.PrimaryMineralId is not null)
    {
      var mineralExists = await _db.Minerals.AsNoTracking().AnyAsync(x => x.Id == listing.PrimaryMineralId, ct);
      if (!mineralExists) missing.Add("PRIMARY_MINERAL_INVALID");
    }

    var images = await _db.ListingMedia
      .AsNoTracking()
      .Where(x => x.ListingId == id
              && x.MediaType == ListingMediaTypes.Image
              && x.Status == ListingMediaStatuses.Ready)
      .ToListAsync(ct);

    if (images.Count < 1) missing.Add("IMAGE_REQUIRED");

    var primaryImages = images.Count(x => x.IsPrimary);
    if (images.Count > 0 && primaryImages != 1) missing.Add("PRIMARY_IMAGE_REQUIRED_EXACTLY_ONE");


    // videos cannot be primary (defensive check)
    var badPrimaryVideo = await _db.ListingMedia.AsNoTracking()
      .AnyAsync(x => x.ListingId == id && x.MediaType == ListingMediaTypes.Video && x.IsPrimary, ct);
    if (badPrimaryVideo) missing.Add("VIDEO_CANNOT_BE_PRIMARY");

    if (missing.Count > 0)
      return BadRequest(new { error = "LISTING_NOT_PUBLISHABLE", missing });

    var now = DateTimeOffset.UtcNow;
    var before = new { status = listing.Status, publishedAt = listing.PublishedAt };

    listing.Status = ListingStatuses.Published;
    listing.PublishedAt ??= now;
    listing.UpdatedAt = now;

    var actorRole =
      User.FindFirstValue(ClaimTypes.Role) ??
      User.FindFirstValue("role");

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();

    await _audit.LogAsync(
      new AuditEvent(
        ActorUserId: actorId,
        ActorRole: actorRole,
        ActionType: "LISTING_PUBLISH",
        EntityType: "LISTING",
        EntityId: listing.Id,
        Before: before,
        After: new { status = listing.Status, publishedAt = listing.PublishedAt },
        IpAddress: ip,
        UserAgent: userAgent
      ),
      ct
    );

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpPost("{id:guid}/archive")]
  public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists) return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var listing = await _db.Listings.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (listing is null) return NotFound(new { error = "LISTING_NOT_FOUND" });

    if (string.Equals(listing.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase))
      return NoContent(); // idempotent

    var now = DateTimeOffset.UtcNow;
    var before = new { status = listing.Status, archivedAt = listing.ArchivedAt };

    listing.Status = ListingStatuses.Archived;
    listing.ArchivedAt ??= now;
    listing.UpdatedAt = now;

    var actorRole =
      User.FindFirstValue(ClaimTypes.Role) ??
      User.FindFirstValue("role");

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();

    await _audit.LogAsync(
      new AuditEvent(
        ActorUserId: actorId,
        ActorRole: actorRole,
        ActionType: "LISTING_ARCHIVE",
        EntityType: "LISTING",
        EntityId: listing.Id,
        Before: before,
        After: new { status = listing.Status, archivedAt = listing.ArchivedAt },
        IpAddress: ip,
        UserAgent: userAgent
      ),
      ct
    );

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  private bool TryGetActorId(out Guid actorId)
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    return Guid.TryParse(raw, out actorId);
  }

  private static bool IsPositive(decimal? v) => v.HasValue && v.Value > 0m;
}
