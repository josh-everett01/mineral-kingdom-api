using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/listings")]
public sealed class ListingsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  public ListingsController(MineralKingdomDbContext db) => _db = db;

  public sealed record MediaDto(Guid Id, string MediaType, string Url, int SortOrder, bool IsPrimary, string? Caption);

  public sealed record ListingDto(
    Guid Id,
    string? Title,
    string? Description,
    string Status,
    Guid? PrimaryMineralId,
    string? CountryCode,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    int? WeightGrams,
    DateTimeOffset? PublishedAt,
    List<MediaDto> Media
  );

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<ListingDto>> Get(Guid id, CancellationToken ct)
  {
    var listing = await _db.Listings.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == id, ct);

    if (listing is null) return NotFound();

    if (!string.Equals(listing.Status, ListingStatuses.Published, StringComparison.OrdinalIgnoreCase))
      return NotFound();

    var media = await _db.ListingMedia.AsNoTracking()
      .Where(x => x.ListingId == id)
      .OrderBy(x => x.SortOrder)
      .Select(x => new MediaDto(x.Id, x.MediaType, x.Url, x.SortOrder, x.IsPrimary, x.Caption))
      .ToListAsync(ct);

    return Ok(new ListingDto(
      listing.Id,
      listing.Title,
      listing.Description,
      listing.Status,
      listing.PrimaryMineralId,
      listing.CountryCode,
      listing.LengthCm,
      listing.WidthCm,
      listing.HeightCm,
      listing.WeightGrams,
      listing.PublishedAt,
      media
    ));
  }
}
