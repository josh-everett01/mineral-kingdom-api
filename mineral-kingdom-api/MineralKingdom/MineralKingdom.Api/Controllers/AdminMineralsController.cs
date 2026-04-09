using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/minerals")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminMineralsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminMineralsController(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public sealed record AdminMineralItemDto(
    Guid Id,
    string Name,
    int ListingCount
  );

  public sealed record CreateAdminMineralRequest(
    string? Name
  );

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<AdminMineralItemDto>>> List(
  [FromQuery] string? query,
  [FromQuery] string? search,
  CancellationToken ct)
  {
    var hasLookupQuery = Request.Query.ContainsKey("query");
    var normalizedQuery = Normalize(query);
    var normalizedSearch = Normalize(search);

    if (hasLookupQuery)
    {
      if (string.IsNullOrWhiteSpace(normalizedQuery))
      {
        return Ok(Array.Empty<AdminMineralItemDto>());
      }

      var lookupItems = await (
        from mineral in _db.Minerals.AsNoTracking()
        join listing in _db.Listings.AsNoTracking()
          on mineral.Id equals listing.PrimaryMineralId into listingGroup
        where mineral.Name.ToLower().Contains(normalizedQuery)
        orderby mineral.Name
        select new AdminMineralItemDto(
          mineral.Id,
          mineral.Name,
          listingGroup.Count()
        ))
        .Take(20)
        .ToListAsync(ct);

      return Ok(lookupItems);
    }

    var queryable =
      from mineral in _db.Minerals.AsNoTracking()
      join listing in _db.Listings.AsNoTracking()
        on mineral.Id equals listing.PrimaryMineralId into listingGroup
      select new
      {
        mineral.Id,
        mineral.Name,
        ListingCount = listingGroup.Count()
      };

    if (!string.IsNullOrWhiteSpace(normalizedSearch))
    {
      queryable = queryable.Where(x => x.Name.ToLower().Contains(normalizedSearch));
    }

    var rows = await queryable
      .OrderBy(x => x.Name)
      .ToListAsync(ct);

    var response = rows
      .Select(x => new AdminMineralItemDto(
        x.Id,
        x.Name,
        x.ListingCount))
      .ToList();

    return Ok(response);
  }

  [HttpPost]
  public async Task<ActionResult<AdminMineralItemDto>> Create(
    [FromBody] CreateAdminMineralRequest req,
    CancellationToken ct)
  {
    if (!TryGetActorId(out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists)
      return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    var normalizedName = Normalize(req.Name);
    if (string.IsNullOrWhiteSpace(normalizedName))
      return BadRequest(new { error = "MINERAL_NAME_REQUIRED" });

    var duplicateExists = await _db.Minerals
      .AsNoTracking()
      .AnyAsync(x => x.Name.ToLower() == normalizedName, ct);

    if (duplicateExists)
      return Conflict(new { error = "MINERAL_NAME_ALREADY_EXISTS" });

    var now = DateTimeOffset.UtcNow;

    var mineral = new Mineral
    {
      Id = Guid.NewGuid(),
      Name = NormalizeDisplayName(req.Name!),
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Minerals.Add(mineral);
    await _db.SaveChangesAsync(ct);

    var response = new AdminMineralItemDto(
      mineral.Id,
      mineral.Name,
      0);

    return Ok(response);
  }

  private static string Normalize(string? value)
  {
    return string.IsNullOrWhiteSpace(value)
      ? string.Empty
      : value.Trim().ToLowerInvariant();
  }

  private static string NormalizeDisplayName(string value)
  {
    return value.Trim();
  }

  private bool TryGetActorId(out Guid actorId)
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    return Guid.TryParse(raw, out actorId);
  }
}