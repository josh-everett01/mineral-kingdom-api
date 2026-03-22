using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auctions")]
public sealed class AuctionRealtimeController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly AuctionBrowseService _browse;
  private readonly AuctionDetailService _detail;

  public AuctionRealtimeController(
    MineralKingdomDbContext db,
    AuctionBrowseService browse,
    AuctionDetailService detail)
  {
    _db = db;
    _browse = browse;
    _detail = detail;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<AuctionBrowseResponseDto>> Browse(CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var dto = await _browse.GetPublicBrowseAsync(now, ct);
    return Ok(dto);
  }

  [HttpGet("{auctionId:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<AuctionRealtimeSnapshot>> GetSnapshot([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var a = await _db.Auctions
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == auctionId, ct);

    if (a is null) return NotFound();

    var hasReserve = a.ReservePriceCents is not null;
    var reserveMet = hasReserve ? a.ReserveMet : (bool?)null;

    var minNext = a.BidCount <= 0
      ? a.StartingPriceCents
      : BidIncrementTable.MinToBeatCents(a.CurrentPriceCents);

    return Ok(new AuctionRealtimeSnapshot(
      AuctionId: a.Id,
      CurrentPriceCents: a.CurrentPriceCents,
      BidCount: a.BidCount,
      ReserveMet: reserveMet,
      Status: a.Status,
      ClosingWindowEnd: a.ClosingWindowEnd,
      MinimumNextBidCents: minNext
    ));
  }

  [HttpGet("{auctionId:guid}/detail")]
  [AllowAnonymous]
  public async Task<ActionResult<AuctionDetailDto>> GetDetail([FromRoute] Guid auctionId, CancellationToken ct)
  {
    var currentUserId = TryGetCurrentUserId();
    var dto = await _detail.GetPublicDetailAsync(auctionId, currentUserId, ct);
    if (dto is null) return NotFound();
    return Ok(dto);
  }

  private Guid? TryGetCurrentUserId()
  {
    var candidates = new[]
    {
    User.FindFirstValue(ClaimTypes.NameIdentifier),
    User.FindFirstValue("sub"),
    User.FindFirstValue("userId"),
    User.FindFirstValue("user_id"),
    User.FindFirstValue("uid"),
    User.Identity?.Name
  };

    foreach (var candidate in candidates)
    {
      if (Guid.TryParse(candidate, out var userId))
        return userId;
    }

    // Last-resort fallback: scan all claim values for a GUID.
    foreach (var claim in User.Claims)
    {
      if (Guid.TryParse(claim.Value, out var userId))
        return userId;
    }

    return null;
  }
}