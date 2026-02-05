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

  [HttpPost("start")]
  [AllowAnonymous]
  public async Task<ActionResult<StartCheckoutResponse>> Start(
    [FromBody] StartCheckoutRequest req,
    [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    // for guests, cartId must come from header or request (support both)
    var cartId = cartIdHeader ?? req.CartId;

    var cart = await _carts.GetOrCreateAsync(userId, cartId, now, ct);

    var (ok, err, hold) = await _checkout.StartCheckoutAsync(cart, userId, now, ct);
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

    var (ok, err) = await _checkout.CompletePaymentAsync(
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
        "PAYMENT_ALREADY_COMPLETED" => Conflict(new { error = err }),
        _ => BadRequest(new { error = err })
      };
    }

    // If we have a cart id available, echo it back for client continuity
    if (cartIdHeader.HasValue)
      Response.Headers["X-Cart-Id"] = cartIdHeader.Value.ToString();

    return NoContent();
  }
}
