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

  public AuctionRealtimeController(MineralKingdomDbContext db) => _db = db;

  // Polling fallback (public)
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

    // Minimum next bid:
    // - if no bidders yet: starting price
    // - else: min to beat current price (+ increment rules)
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
}