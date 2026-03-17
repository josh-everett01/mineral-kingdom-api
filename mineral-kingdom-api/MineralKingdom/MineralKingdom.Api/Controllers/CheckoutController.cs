using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
  private readonly CartService _carts;
  private readonly CheckoutService _checkout;

  public CheckoutController(CartService carts, CheckoutService checkout)
  {
    _carts = carts;
    _checkout = checkout;
  }

  private Guid? TryGetUserId()
  {
    var raw = User.FindFirst("sub")?.Value
           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    return Guid.TryParse(raw, out var id) ? id : null;
  }

  [HttpGet("active")]
  [AllowAnonymous]
  public async Task<ActionResult<ActiveCheckoutResponse>> Active(
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);
    var (ok, err, hold) = await _checkout.GetActiveCheckoutAsync(cart, userId, now, ct);

    if (!ok)
    {
      return err switch
      {
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(new { error = err })
      };
    }

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();

    return Ok(new ActiveCheckoutResponse(
      Active: hold is not null,
      CartId: cart.Id,
      HoldId: hold?.Id,
      ExpiresAt: hold?.ExpiresAt,
      GuestEmail: hold?.GuestEmail,
      Status: hold?.Status,
      CanExtend: hold is not null && _checkout.CanExtend(hold, now),
      ExtensionCount: hold?.ExtensionCount ?? 0,
      MaxExtensions: _checkout.MaxExtensions
    ));
  }

  [HttpPost("reset")]
  [AllowAnonymous]
  public async Task<ActionResult<ResetCheckoutResponse>> Reset(
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);
    var (ok, err) = await _checkout.ResetActiveCheckoutAsync(cart, userId, now, ct);

    if (!ok)
    {
      return err switch
      {
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(new { error = err })
      };
    }

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();

    return Ok(new ResetCheckoutResponse(
      Reset: true,
      CartId: cart.Id
    ));
  }

  [HttpPost("start")]
  [AllowAnonymous]
  public async Task<ActionResult<StartCheckoutResponse>> Start(
    [FromBody] StartCheckoutRequest req,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;
    var cartId = cartIdHeader ?? req.CartId;

    var cart = await _carts.GetOrCreateAsync(userId, cartId, now, ct);

    var (ok, err, hold) = await _checkout.StartCheckoutAsync(cart, userId, req.Email, now, ct);
    if (!ok) return BadRequest(new { error = err });

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();

    return Ok(new StartCheckoutResponse(
      CartId: cart.Id,
      HoldId: hold!.Id,
      ExpiresAt: hold.ExpiresAt
    ));
  }

  [HttpPost("complete")]
  [AllowAnonymous]
  public async Task<ActionResult> Complete(
    [FromBody] CompleteCheckoutRequest req,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var (ok, err) = await _checkout.RecordClientReturnAsync(
      req.HoldId,
      userId,
      req.PaymentReference,
      now,
      ct);

    if (!ok)
    {
      return err switch
      {
        "HOLD_NOT_FOUND" => NotFound(new { error = err }),
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(new { error = err })
      };
    }

    if (cartIdHeader.HasValue)
      Response.Headers["X-Cart-Id"] = cartIdHeader.Value.ToString();

    return NoContent();
  }

  [HttpPost("heartbeat")]
  [AllowAnonymous]
  public async Task<ActionResult<CheckoutHeartbeatResponse>> Heartbeat(
    [FromBody] CheckoutHeartbeatRequest req,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var (ok, err, hold) = await _checkout.HeartbeatAsync(req.HoldId, userId, now, ct);
    if (!ok)
    {
      return err switch
      {
        "HOLD_NOT_FOUND" => NotFound(new { error = err }),
        "HOLD_EXPIRED" => BadRequest(new { error = err }),
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(new { error = err })
      };
    }

    return Ok(new CheckoutHeartbeatResponse(
      hold!.Id,
      hold.ExpiresAt,
      _checkout.CanExtend(hold, now),
      hold.ExtensionCount,
      _checkout.MaxExtensions
    ));
  }

  [HttpPost("extend")]
  [AllowAnonymous]
  public async Task<ActionResult<ExtendCheckoutResponse>> Extend(
    [FromBody] ExtendCheckoutRequest req,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var (ok, err, hold) = await _checkout.ExtendHoldAsync(req.HoldId, userId, now, ct);
    if (!ok)
    {
      return err switch
      {
        "HOLD_NOT_FOUND" => NotFound(new { error = err }),
        "HOLD_EXPIRED" => BadRequest(new { error = err }),
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(new { error = err })
      };
    }

    return Ok(new ExtendCheckoutResponse(
      hold!.Id,
      hold.ExpiresAt,
      _checkout.CanExtend(hold, now),
      hold.ExtensionCount,
      _checkout.MaxExtensions
    ));
  }
}