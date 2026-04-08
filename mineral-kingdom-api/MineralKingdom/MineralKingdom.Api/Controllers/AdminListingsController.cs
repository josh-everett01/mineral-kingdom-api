using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
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

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<AdminListingListItemDto>>> List(CancellationToken ct)
  {
    var rows = await (
      from listing in _db.Listings.AsNoTracking()
      join mineral in _db.Minerals.AsNoTracking()
        on listing.PrimaryMineralId equals mineral.Id into mineralJoin
      from mineral in mineralJoin.DefaultIfEmpty()
      orderby listing.UpdatedAt descending, listing.CreatedAt descending
      select new
      {
        listing.Id,
        listing.Title,
        listing.Status,
        listing.PrimaryMineralId,
        PrimaryMineralName = mineral != null ? mineral.Name : null,
        listing.LocalityDisplay,
        listing.QuantityAvailable,
        listing.QuantityTotal,
        listing.UpdatedAt,
        listing.PublishedAt,
        listing.ArchivedAt,
        listing.Description,
        listing.CountryCode,
        listing.LengthCm,
        listing.WidthCm,
        listing.HeightCm
      })
      .ToListAsync(ct);

    var listingIds = rows.Select(x => x.Id).ToList();

    var readyImageCounts = await _db.ListingMedia
      .AsNoTracking()
      .Where(x =>
        listingIds.Contains(x.ListingId) &&
        x.MediaType == ListingMediaTypes.Image &&
        x.Status == ListingMediaStatuses.Ready &&
        x.DeletedAt == null)
      .GroupBy(x => x.ListingId)
      .Select(g => new { ListingId = g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.ListingId, x => x.Count, ct);

    var primaryReadyImageCounts = await _db.ListingMedia
      .AsNoTracking()
      .Where(x =>
        listingIds.Contains(x.ListingId) &&
        x.MediaType == ListingMediaTypes.Image &&
        x.Status == ListingMediaStatuses.Ready &&
        x.DeletedAt == null &&
        x.IsPrimary)
      .GroupBy(x => x.ListingId)
      .Select(g => new { ListingId = g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.ListingId, x => x.Count, ct);

    var badPrimaryVideoIds = await _db.ListingMedia
      .AsNoTracking()
      .Where(x =>
        listingIds.Contains(x.ListingId) &&
        x.MediaType == ListingMediaTypes.Video &&
        x.IsPrimary &&
        x.DeletedAt == null)
      .Select(x => x.ListingId)
      .Distinct()
      .ToListAsync(ct);

    var primaryMineralIds = rows
  .Where(r => r.PrimaryMineralId.HasValue)
  .Select(r => r.PrimaryMineralId!.Value)
  .Distinct()
  .ToList();

    var validMineralIds = primaryMineralIds.Count == 0
      ? new List<Guid>()
      : await _db.Minerals
          .AsNoTracking()
          .Where(x => primaryMineralIds.Contains(x.Id))
          .Select(x => x.Id)
          .ToListAsync(ct);

    var validMineralIdSet = validMineralIds.ToHashSet();
    var badPrimaryVideoIdSet = badPrimaryVideoIds.ToHashSet();

    var result = rows
      .Select(x =>
      {
        var checklist = BuildPublishChecklist(
          title: x.Title,
          description: x.Description,
          primaryMineralId: x.PrimaryMineralId,
          hasValidPrimaryMineral: !x.PrimaryMineralId.HasValue || validMineralIdSet.Contains(x.PrimaryMineralId.Value),
          countryCode: x.CountryCode,
          lengthCm: x.LengthCm,
          widthCm: x.WidthCm,
          heightCm: x.HeightCm,
          readyImageCount: readyImageCounts.GetValueOrDefault(x.Id, 0),
          primaryReadyImageCount: primaryReadyImageCounts.GetValueOrDefault(x.Id, 0),
          hasPrimaryVideoViolation: badPrimaryVideoIdSet.Contains(x.Id)
        );

        return new AdminListingListItemDto(
          Id: x.Id,
          Title: x.Title,
          Status: x.Status,
          PrimaryMineralId: x.PrimaryMineralId,
          PrimaryMineralName: x.PrimaryMineralName,
          LocalityDisplay: x.LocalityDisplay,
          QuantityAvailable: x.QuantityAvailable,
          QuantityTotal: x.QuantityTotal,
          UpdatedAt: x.UpdatedAt,
          PublishedAt: x.PublishedAt,
          ArchivedAt: x.ArchivedAt,
          PublishChecklist: checklist
        );
      })
      .ToList();

    return Ok(result);
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<AdminListingDetailDto>> Get(Guid id, CancellationToken ct)
  {
    var listing = await _db.Listings
      .AsNoTracking()
      .Include(x => x.PrimaryMineral)
      .SingleOrDefaultAsync(x => x.Id == id, ct);

    if (listing is null)
      return NotFound(new { error = "LISTING_NOT_FOUND" });

    var readyImageCount = await _db.ListingMedia
      .AsNoTracking()
      .CountAsync(x =>
        x.ListingId == id &&
        x.MediaType == ListingMediaTypes.Image &&
        x.Status == ListingMediaStatuses.Ready &&
        x.DeletedAt == null, ct);

    var primaryReadyImageCount = await _db.ListingMedia
      .AsNoTracking()
      .CountAsync(x =>
        x.ListingId == id &&
        x.MediaType == ListingMediaTypes.Image &&
        x.Status == ListingMediaStatuses.Ready &&
        x.DeletedAt == null &&
        x.IsPrimary, ct);

    var hasPrimaryVideoViolation = await _db.ListingMedia
      .AsNoTracking()
      .AnyAsync(x =>
        x.ListingId == id &&
        x.MediaType == ListingMediaTypes.Video &&
        x.IsPrimary &&
        x.DeletedAt == null, ct);

    var hasValidPrimaryMineral = true;
    if (listing.PrimaryMineralId.HasValue)
    {
      hasValidPrimaryMineral = await _db.Minerals
        .AsNoTracking()
        .AnyAsync(x => x.Id == listing.PrimaryMineralId.Value, ct);
    }

    var checklist = BuildPublishChecklist(
      title: listing.Title,
      description: listing.Description,
      primaryMineralId: listing.PrimaryMineralId,
      hasValidPrimaryMineral: hasValidPrimaryMineral,
      countryCode: listing.CountryCode,
      lengthCm: listing.LengthCm,
      widthCm: listing.WidthCm,
      heightCm: listing.HeightCm,
      readyImageCount: readyImageCount,
      primaryReadyImageCount: primaryReadyImageCount,
      hasPrimaryVideoViolation: hasPrimaryVideoViolation
    );

    var dto = new AdminListingDetailDto(
      Id: listing.Id,
      Status: listing.Status,
      Title: listing.Title,
      Description: listing.Description,
      PrimaryMineralId: listing.PrimaryMineralId,
      PrimaryMineralName: listing.PrimaryMineral?.Name,
      LocalityDisplay: listing.LocalityDisplay,
      CountryCode: listing.CountryCode,
      AdminArea1: listing.AdminArea1,
      AdminArea2: listing.AdminArea2,
      MineName: listing.MineName,
      LengthCm: listing.LengthCm,
      WidthCm: listing.WidthCm,
      HeightCm: listing.HeightCm,
      WeightGrams: listing.WeightGrams,
      SizeClass: listing.SizeClass,
      IsFluorescent: listing.IsFluorescent,
      FluorescenceNotes: listing.FluorescenceNotes,
      ConditionNotes: listing.ConditionNotes,
      IsLot: listing.IsLot,
      QuantityTotal: listing.QuantityTotal,
      QuantityAvailable: listing.QuantityAvailable,
      UpdatedAt: listing.UpdatedAt,
      PublishedAt: listing.PublishedAt,
      ArchivedAt: listing.ArchivedAt,
      MediaSummary: new AdminListingMediaSummaryDto(
        ReadyImageCount: readyImageCount,
        PrimaryReadyImageCount: primaryReadyImageCount,
        HasPrimaryVideoViolation: hasPrimaryVideoViolation
      ),
      PublishChecklist: checklist
    );

    return Ok(dto);
  }

  [HttpPost]
  public async Task<ActionResult<ListingIdResponse>> Create([FromBody] CreateListingRequest req, CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

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
      return NoContent();

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
              && x.Status == ListingMediaStatuses.Ready
              && x.DeletedAt == null)
      .ToListAsync(ct);

    if (images.Count < 1) missing.Add("IMAGE_REQUIRED");

    var primaryImages = images.Count(x => x.IsPrimary);
    if (images.Count > 0 && primaryImages != 1) missing.Add("PRIMARY_IMAGE_REQUIRED_EXACTLY_ONE");

    var badPrimaryVideo = await _db.ListingMedia.AsNoTracking()
      .AnyAsync(x => x.ListingId == id && x.MediaType == ListingMediaTypes.Video && x.IsPrimary && x.DeletedAt == null, ct);
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
      return NoContent();

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

  private static AdminListingPublishChecklistDto BuildPublishChecklist(
    string? title,
    string? description,
    Guid? primaryMineralId,
    bool hasValidPrimaryMineral,
    string? countryCode,
    decimal? lengthCm,
    decimal? widthCm,
    decimal? heightCm,
    int readyImageCount,
    int primaryReadyImageCount,
    bool hasPrimaryVideoViolation)
  {
    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(title)) missing.Add("TITLE");
    if (string.IsNullOrWhiteSpace(description)) missing.Add("DESCRIPTION");
    if (primaryMineralId is null) missing.Add("PRIMARY_MINERAL");
    if (primaryMineralId is not null && !hasValidPrimaryMineral) missing.Add("PRIMARY_MINERAL_INVALID");
    if (string.IsNullOrWhiteSpace(countryCode)) missing.Add("COUNTRY");
    if (!IsPositive(lengthCm)) missing.Add("LENGTH_CM");
    if (!IsPositive(widthCm)) missing.Add("WIDTH_CM");
    if (!IsPositive(heightCm)) missing.Add("HEIGHT_CM");
    if (readyImageCount < 1) missing.Add("IMAGE_REQUIRED");
    if (readyImageCount > 0 && primaryReadyImageCount != 1) missing.Add("PRIMARY_IMAGE_REQUIRED_EXACTLY_ONE");
    if (hasPrimaryVideoViolation) missing.Add("VIDEO_CANNOT_BE_PRIMARY");

    return new AdminListingPublishChecklistDto(
      CanPublish: missing.Count == 0,
      Missing: missing
    );
  }
}