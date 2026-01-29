using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auctions/{auctionId:guid}/bids")]
public sealed class AuctionBidsController : ControllerBase
{
  // Placeholder endpoint to prove server-side verification enforcement.
  // The full auction module will implement bidding rules in a later story.

  public sealed record PlaceBidRequest(int MaxBid, string Mode);

  [HttpPost]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified)]
  public ActionResult PlaceBid([FromRoute] Guid auctionId, [FromBody] PlaceBidRequest req)
  {
    return StatusCode(StatusCodes.Status501NotImplemented);
  }
}
