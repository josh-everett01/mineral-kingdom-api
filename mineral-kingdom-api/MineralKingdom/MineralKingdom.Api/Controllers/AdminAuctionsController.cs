using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/auctions")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminAuctionsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionAdminService _svc;

  public AdminAuctionsController(MineralKingdomDbContext db, AuctionAdminService svc)
  {
    _db = db;
    _svc = svc;
  }

  [HttpPost]
  public async Task<ActionResult<AuctionIdResponse>> Create([FromBody] CreateAuctionRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var (ok, err, id) = await _svc.CreateDraftAsync(req, now, ct);
    if (!ok) return BadRequest(new { error = err });
    return Ok(new AuctionIdResponse(id!.Value));
  }

  [HttpGet]
  public async Task<ActionResult<List<AdminAuctionListItemDto>>> List(
    [FromQuery] string? status,
    [FromQuery] string? search,
    CancellationToken ct)
  {
    var query =
      from auction in _db.Auctions.AsNoTracking()
      join listing in _db.Listings.AsNoTracking() on auction.ListingId equals listing.Id
      select new
      {
        auction.Id,
        auction.ListingId,
        ListingTitle = listing.Title,
        auction.Status,
        auction.StartingPriceCents,
        auction.CurrentPriceCents,
        auction.ReservePriceCents,
        auction.ReserveMet,
        auction.BidCount,
        auction.StartTime,
        auction.CloseTime,
        auction.ClosingWindowEnd,
        auction.QuotedShippingCents,
        auction.RelistOfAuctionId,
        auction.CreatedAt,
        auction.UpdatedAt
      };

    if (!string.IsNullOrWhiteSpace(status))
    {
      var normalizedStatus = status.Trim().ToUpperInvariant();
      query = query.Where(x => x.Status == normalizedStatus);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(x => (x.ListingTitle ?? "").ToLower().Contains(term));
    }

    var rows = await query
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync(ct);

    var result = rows
      .Select(x => new AdminAuctionListItemDto(
        Id: x.Id,
        ListingId: x.ListingId,
        ListingTitle: x.ListingTitle,
        Status: x.Status,
        StartingPriceCents: x.StartingPriceCents,
        CurrentPriceCents: x.CurrentPriceCents,
        ReservePriceCents: x.ReservePriceCents,
        HasReserve: x.ReservePriceCents is not null,
        ReserveMet: x.ReservePriceCents is not null ? x.ReserveMet : null,
        BidCount: x.BidCount,
        StartTime: x.StartTime,
        CloseTime: x.CloseTime,
        ClosingWindowEnd: x.ClosingWindowEnd,
        QuotedShippingCents: x.QuotedShippingCents,
        RelistOfAuctionId: x.RelistOfAuctionId,
        CreatedAt: x.CreatedAt,
        UpdatedAt: x.UpdatedAt
      ))
      .ToList();

    return Ok(result);
  }

  [HttpPatch("{auctionId:guid}")]
  public async Task<IActionResult> Patch([FromRoute] Guid auctionId, [FromBody] UpdateAuctionRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var (ok, err) = await _svc.UpdateAsync(auctionId, req, now, ct);
    if (!ok) return BadRequest(new { error = err });
    return Ok();
  }

  [HttpPost("{auctionId:guid}/start")]
  [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
  public async Task<IActionResult> Start([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var (ok, err) = await _svc.StartAsync(auctionId, now, ct);
    if (!ok) return BadRequest(new { error = err });
    return Ok();
  }

  [HttpGet("{auctionId:guid}")]
  public async Task<ActionResult<AdminAuctionDetailDto>> Get([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var row = await (
      from auction in _db.Auctions.AsNoTracking()
      join listing in _db.Listings.AsNoTracking() on auction.ListingId equals listing.Id
      where auction.Id == auctionId
      select new
      {
        auction.Id,
        auction.ListingId,
        ListingTitle = listing.Title,
        auction.Status,
        auction.StartingPriceCents,
        auction.CurrentPriceCents,
        auction.ReservePriceCents,
        auction.ReserveMet,
        auction.BidCount,
        auction.StartTime,
        auction.CloseTime,
        auction.ClosingWindowEnd,
        auction.QuotedShippingCents,
        auction.RelistOfAuctionId,
        auction.CreatedAt,
        auction.UpdatedAt
      })
      .SingleOrDefaultAsync(ct);

    if (row is null) return NotFound(new { error = "AUCTION_NOT_FOUND" });

    var replacementAuctionId = await _db.Auctions.AsNoTracking()
      .Where(x => x.RelistOfAuctionId == row.Id)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => (Guid?)x.Id)
      .FirstOrDefaultAsync(ct);

    var hasReserve = row.ReservePriceCents is not null;
    bool? reserveMet = hasReserve ? row.ReserveMet : null;

    var isCloseDue = row.Status == AuctionStatuses.Live && row.CloseTime <= now;
    var secondsUntilCloseDue = (int)Math.Ceiling((row.CloseTime - now).TotalSeconds);
    if (secondsUntilCloseDue < 0) secondsUntilCloseDue = 0;

    var isClosingWindowDue =
      row.Status == AuctionStatuses.Closing &&
      row.ClosingWindowEnd is not null &&
      row.ClosingWindowEnd.Value <= now;

    int? secondsUntilClosingWindowDue = null;
    if (row.ClosingWindowEnd is not null)
    {
      var s = (int)Math.Ceiling((row.ClosingWindowEnd.Value - now).TotalSeconds);
      secondsUntilClosingWindowDue = s < 0 ? 0 : s;
    }

    var relistDelay = TimeSpan.FromMinutes(10);
    var relistDueAt = row.UpdatedAt.Add(relistDelay);

    var isRelistCandidate =
      row.Status == AuctionStatuses.ClosedNotSold &&
      row.RelistOfAuctionId is null &&
      hasReserve &&
      row.ReserveMet == false;

    var isRelistDue = isRelistCandidate && relistDueAt <= now;

    var secondsUntilRelistDue = (int)Math.Ceiling((relistDueAt - now).TotalSeconds);
    if (secondsUntilRelistDue < 0) secondsUntilRelistDue = 0;

    return Ok(new AdminAuctionDetailDto(
      AuctionId: row.Id,
      ListingId: row.ListingId,
      ListingTitle: row.ListingTitle,
      Status: row.Status,
      StartingPriceCents: row.StartingPriceCents,
      CurrentPriceCents: row.CurrentPriceCents,
      ReservePriceCents: row.ReservePriceCents,
      HasReserve: hasReserve,
      ReserveMet: reserveMet,
      BidCount: row.BidCount,
      StartTime: row.StartTime,
      CloseTime: row.CloseTime,
      ClosingWindowEnd: row.ClosingWindowEnd,
      QuotedShippingCents: row.QuotedShippingCents,
      RelistOfAuctionId: row.RelistOfAuctionId,
      ReplacementAuctionId: replacementAuctionId,
      CreatedAt: row.CreatedAt,
      UpdatedAt: row.UpdatedAt,
      ServerTimeUtc: now,
      IsCloseDue: isCloseDue,
      SecondsUntilCloseDue: secondsUntilCloseDue,
      IsClosingWindowDue: isClosingWindowDue,
      SecondsUntilClosingWindowDue: secondsUntilClosingWindowDue,
      IsRelistDue: isRelistDue,
      SecondsUntilRelistDue: secondsUntilRelistDue
    ));
  }

  [HttpGet("relist-of/{oldAuctionId:guid}")]
  public async Task<ActionResult<AdminAuctionDetailDto>> GetRelist([FromRoute] Guid oldAuctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var row = await (
      from auction in _db.Auctions.AsNoTracking()
      join listing in _db.Listings.AsNoTracking() on auction.ListingId equals listing.Id
      where auction.RelistOfAuctionId == oldAuctionId
      orderby auction.CreatedAt descending
      select new
      {
        auction.Id,
        auction.ListingId,
        ListingTitle = listing.Title,
        auction.Status,
        auction.StartingPriceCents,
        auction.CurrentPriceCents,
        auction.ReservePriceCents,
        auction.ReserveMet,
        auction.BidCount,
        auction.StartTime,
        auction.CloseTime,
        auction.ClosingWindowEnd,
        auction.QuotedShippingCents,
        auction.RelistOfAuctionId,
        auction.CreatedAt,
        auction.UpdatedAt
      })
      .FirstOrDefaultAsync(ct);

    if (row is null) return NotFound(new { error = "RELIST_NOT_FOUND" });

    var replacementAuctionId = await _db.Auctions.AsNoTracking()
      .Where(x => x.RelistOfAuctionId == row.Id)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => (Guid?)x.Id)
      .FirstOrDefaultAsync(ct);

    var hasReserve = row.ReservePriceCents is not null;
    bool? reserveMet = hasReserve ? row.ReserveMet : null;

    var isCloseDue = row.Status == AuctionStatuses.Live && row.CloseTime <= now;
    var secondsUntilCloseDue = (int)Math.Ceiling((row.CloseTime - now).TotalSeconds);
    if (secondsUntilCloseDue < 0) secondsUntilCloseDue = 0;

    var isClosingWindowDue =
      row.Status == AuctionStatuses.Closing &&
      row.ClosingWindowEnd is not null &&
      row.ClosingWindowEnd.Value <= now;

    int? secondsUntilClosingWindowDue = null;
    if (row.ClosingWindowEnd is not null)
    {
      var s = (int)Math.Ceiling((row.ClosingWindowEnd.Value - now).TotalSeconds);
      secondsUntilClosingWindowDue = s < 0 ? 0 : s;
    }

    var relistDelay = TimeSpan.FromMinutes(10);
    var relistDueAt = row.UpdatedAt.Add(relistDelay);

    var isRelistCandidate =
      row.Status == AuctionStatuses.ClosedNotSold &&
      row.RelistOfAuctionId is null &&
      hasReserve &&
      row.ReserveMet == false;

    var isRelistDue = isRelistCandidate && relistDueAt <= now;

    var secondsUntilRelistDue = (int)Math.Ceiling((relistDueAt - now).TotalSeconds);
    if (secondsUntilRelistDue < 0) secondsUntilRelistDue = 0;

    return Ok(new AdminAuctionDetailDto(
      AuctionId: row.Id,
      ListingId: row.ListingId,
      ListingTitle: row.ListingTitle,
      Status: row.Status,
      StartingPriceCents: row.StartingPriceCents,
      CurrentPriceCents: row.CurrentPriceCents,
      ReservePriceCents: row.ReservePriceCents,
      HasReserve: hasReserve,
      ReserveMet: reserveMet,
      BidCount: row.BidCount,
      StartTime: row.StartTime,
      CloseTime: row.CloseTime,
      ClosingWindowEnd: row.ClosingWindowEnd,
      QuotedShippingCents: row.QuotedShippingCents,
      RelistOfAuctionId: row.RelistOfAuctionId,
      ReplacementAuctionId: replacementAuctionId,
      CreatedAt: row.CreatedAt,
      UpdatedAt: row.UpdatedAt,
      ServerTimeUtc: now,
      IsCloseDue: isCloseDue,
      SecondsUntilCloseDue: secondsUntilCloseDue,
      IsClosingWindowDue: isClosingWindowDue,
      SecondsUntilClosingWindowDue: secondsUntilClosingWindowDue,
      IsRelistDue: isRelistDue,
      SecondsUntilRelistDue: secondsUntilRelistDue
    ));
  }
}