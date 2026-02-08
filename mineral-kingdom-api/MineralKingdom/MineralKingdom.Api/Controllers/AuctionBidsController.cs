using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Infrastructure.Auctions;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auctions/{auctionId:guid}/bids")]
public sealed class AuctionBidsController : ControllerBase
{
  private readonly AuctionBiddingService _bids;

  public AuctionBidsController(AuctionBiddingService bids) => _bids = bids;

  public sealed record PlaceBidRequest(int MaxBidCents, string Mode);

  public sealed record PlaceBidResponse(
    int CurrentPriceCents,
    Guid? LeaderUserId,
    bool ReserveMet
  );

  [HttpPost]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified)]
  public async Task<ActionResult<PlaceBidResponse>> PlaceBid(
    [FromRoute] Guid auctionId,
    [FromBody] PlaceBidRequest req,
    CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var result = await _bids.PlaceBidAsync(auctionId, userId, req.MaxBidCents, req.Mode, now, ct);

    if (!result.Ok)
      return BadRequest(new { error = result.Error });

    return Ok(new PlaceBidResponse(
      CurrentPriceCents: result.CurrentPriceCents!.Value,
      LeaderUserId: result.LeaderUserId,
      ReserveMet: result.ReserveMet!.Value
    ));
  }
}
