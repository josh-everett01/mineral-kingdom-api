using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/auctions")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminAuctionsController : ControllerBase
{
  private readonly AuctionAdminService _svc;

  public AdminAuctionsController(AuctionAdminService svc) => _svc = svc;

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
}
