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

  [HttpPost("{auctionId:guid}/start")]
  public async Task<IActionResult> Start([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var (ok, err) = await _svc.StartAsync(auctionId, now, ct);
    if (!ok) return BadRequest(new { error = err });
    return Ok();
  }

  public sealed record AdminAuctionResponse(
  Guid Id,
  Guid ListingId,
  string Status,
  int StartingPriceCents,
  int? ReservePriceCents,

  // ✅ clarity: reserve may not apply
  bool HasReserve,
  bool? ReserveMet,

  int BidCount,
  DateTimeOffset? StartTime,
  DateTimeOffset CloseTime,
  DateTimeOffset? ClosingWindowEnd,
  Guid? RelistOfAuctionId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,

  // ✅ server time (source of truth for due calculations)
  DateTimeOffset ServerTimeUtc,

  // ✅ due helpers (computed relative to ServerTimeUtc)
  bool IsCloseDue,
  int SecondsUntilCloseDue,

  bool IsClosingWindowDue,
  int? SecondsUntilClosingWindowDue,

  bool IsRelistDue,
  int SecondsUntilRelistDue
);



  [HttpGet("{auctionId:guid}")]
  public async Task<ActionResult<AdminAuctionResponse>> Get([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var a = await _db.Auctions
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == auctionId, ct);

    if (a is null) return NotFound(new { error = "AUCTION_NOT_FOUND" });

    var hasReserve = a.ReservePriceCents is not null;
    bool? reserveMet = hasReserve ? a.ReserveMet : null;


    // ----- Due helpers -----
    var isCloseDue = a.Status == AuctionStatuses.Live && a.CloseTime <= now;
    var secondsUntilCloseDue = (int)Math.Ceiling((a.CloseTime - now).TotalSeconds);
    if (secondsUntilCloseDue < 0) secondsUntilCloseDue = 0;

    var isClosingWindowDue =
      a.Status == AuctionStatuses.Closing &&
      a.ClosingWindowEnd is not null &&
      a.ClosingWindowEnd.Value <= now;

    int? secondsUntilClosingWindowDue = null;
    if (a.ClosingWindowEnd is not null)
    {
      var s = (int)Math.Ceiling((a.ClosingWindowEnd.Value - now).TotalSeconds);
      secondsUntilClosingWindowDue = s < 0 ? 0 : s;
    }

    // Relist rules must match AuctionStateMachineService:
    // - status CLOSED_NOT_SOLD
    // - reserve configured
    // - reserve NOT met
    // - not already relisted
    // - UpdatedAt <= now - RelistDelay
    var relistDelay = TimeSpan.FromMinutes(10);
    var relistDueAt = a.UpdatedAt.Add(relistDelay);

    var isRelistCandidate =
      a.Status == AuctionStatuses.ClosedNotSold &&
      a.RelistOfAuctionId is null &&
      hasReserve &&
      a.ReserveMet == false;


    var isRelistDue = isRelistCandidate && relistDueAt <= now;

    var secondsUntilRelistDue = (int)Math.Ceiling((relistDueAt - now).TotalSeconds);
    if (secondsUntilRelistDue < 0) secondsUntilRelistDue = 0;

    return Ok(new AdminAuctionResponse(
      a.Id,
      a.ListingId,
      a.Status,
      a.StartingPriceCents,
      a.ReservePriceCents,

      HasReserve: hasReserve,
      ReserveMet: reserveMet,

      a.BidCount,
      a.StartTime,
      a.CloseTime,
      a.ClosingWindowEnd,
      a.RelistOfAuctionId,
      a.CreatedAt,
      a.UpdatedAt,

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
  public async Task<ActionResult<AdminAuctionResponse>> GetRelist([FromRoute] Guid oldAuctionId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var relisted = await _db.Auctions.AsNoTracking()
      .Where(x => x.RelistOfAuctionId == oldAuctionId)
      .OrderByDescending(x => x.CreatedAt)
      .FirstOrDefaultAsync(ct);

    if (relisted is null) return NotFound(new { error = "RELIST_NOT_FOUND" });

    var hasReserve = relisted.ReservePriceCents is not null;
    bool? reserveMet = hasReserve ? relisted.ReserveMet : null;

    // ----- Due helpers -----
    var isCloseDue = relisted.Status == AuctionStatuses.Live && relisted.CloseTime <= now;
    var secondsUntilCloseDue = (int)Math.Ceiling((relisted.CloseTime - now).TotalSeconds);
    if (secondsUntilCloseDue < 0) secondsUntilCloseDue = 0;

    var isClosingWindowDue =
      relisted.Status == AuctionStatuses.Closing &&
      relisted.ClosingWindowEnd is not null &&
      relisted.ClosingWindowEnd.Value <= now;

    int? secondsUntilClosingWindowDue = null;
    if (relisted.ClosingWindowEnd is not null)
    {
      var s = (int)Math.Ceiling((relisted.ClosingWindowEnd.Value - now).TotalSeconds);
      secondsUntilClosingWindowDue = s < 0 ? 0 : s;
    }

    var relistDelay = TimeSpan.FromMinutes(10);
    var relistDueAt = relisted.UpdatedAt.Add(relistDelay);

    var isRelistCandidate =
        relisted.Status == AuctionStatuses.ClosedNotSold &&
        relisted.RelistOfAuctionId is null &&
        hasReserve &&
        relisted.ReserveMet == false;

    var isRelistDue = isRelistCandidate && relistDueAt <= now;

    var secondsUntilRelistDue = (int)Math.Ceiling((relistDueAt - now).TotalSeconds);
    if (secondsUntilRelistDue < 0) secondsUntilRelistDue = 0;

    return Ok(new AdminAuctionResponse(
    relisted.Id,
    relisted.ListingId,
    relisted.Status,
    relisted.StartingPriceCents,
    relisted.ReservePriceCents,

    HasReserve: hasReserve,
    ReserveMet: reserveMet,

    relisted.BidCount,
    relisted.StartTime,
    relisted.CloseTime,
    relisted.ClosingWindowEnd,
    relisted.RelistOfAuctionId,
    relisted.CreatedAt,
    relisted.UpdatedAt,

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
