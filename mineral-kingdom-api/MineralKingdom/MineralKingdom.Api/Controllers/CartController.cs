using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly CartService _carts;

  public CartController(MineralKingdomDbContext db, CartService carts)
  {
    _db = db;
    _carts = carts;
  }

  private Guid? TryGetUserId()
  {
    var raw = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(raw, out var id) ? id : null;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> Get(
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();
    return Ok(await _carts.ToDtoAsync(cart, ct));
  }

  [HttpPut("lines")]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> UpsertLine(
    [FromBody] UpsertCartLineRequest req,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);
    var (ok, err) = await _carts.UpsertLineAsync(cart.Id, userId, req.OfferId, req.Quantity, now, ct);
    if (!ok) return BadRequest(new { error = err });

    var refreshed = await _carts.GetCartForResponseAsync(cart.Id, userId, ct);
    if (refreshed is null) return StatusCode(500, new { error = "CART_REFRESH_FAILED" });

    Response.Headers["X-Cart-Id"] = refreshed.Id.ToString();
    return Ok(await _carts.ToDtoAsync(refreshed, ct));
  }

  [HttpDelete("lines/{offerId:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> RemoveLine(
    [FromRoute] Guid offerId,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);

    var (ok, err) = await _carts.RemoveLineAsync(cart, offerId, now, ct);
    if (!ok) return BadRequest(new { error = err });

    var refreshed = await _carts.GetCartForResponseAsync(cart.Id, userId, ct);
    if (refreshed is null) return StatusCode(500, new { error = "CART_REFRESH_FAILED" });

    Response.Headers["X-Cart-Id"] = refreshed.Id.ToString();
    return Ok(await _carts.ToDtoAsync(refreshed, ct));
  }

  [HttpPost("notices/{noticeId:guid}/dismiss")]
  [AllowAnonymous]
  public async Task<ActionResult<DismissCartNoticeResponse>> DismissNotice(
    Guid noticeId,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);
    var (ok, err) = await _carts.DismissNoticeAsync(cart.Id, noticeId, now, ct);

    if (!ok)
    {
      return err switch
      {
        "NOTICE_NOT_FOUND" => NotFound(new { error = err }),
        _ => BadRequest(new { error = err })
      };
    }

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();

    return Ok(new DismissCartNoticeResponse(true, noticeId));
  }
}