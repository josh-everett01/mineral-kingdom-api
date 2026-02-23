using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = UserRoles.Owner)]
public sealed class AdminOrdersController : ControllerBase
{
  private readonly OrderService _orders;
  private readonly FulfillmentService _fulfillment;

  public AdminOrdersController(OrderService orders, FulfillmentService fulfillment)
  {
    _orders = orders;
    _fulfillment = fulfillment;
  }

  [HttpPost("{id:guid}/payment-due")]
  public async Task<IActionResult> ExtendPaymentDue(Guid id, [FromBody] ExtendPaymentDueRequest req, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    // Basic validation
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });
    if (req.PaymentDueAt <= now) return BadRequest(new { error = "PAYMENT_DUE_MUST_BE_IN_FUTURE" });

    // Capture request context for audit
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _orders.AdminExtendAuctionPaymentDueAsync(
      id,
      req.PaymentDueAt,
      adminUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("{id:guid}/fulfillment/packed")]
  public async Task<IActionResult> MarkPacked(Guid id, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkPackedAsync(id, adminUserId, now, ip, userAgent, ct);
    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("{id:guid}/fulfillment/shipped")]
  public async Task<IActionResult> MarkShipped(Guid id, [FromBody] AdminMarkShippedRequest req, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkShippedAsync(
      id,
      req.ShippingCarrier,
      req.TrackingNumber,
      adminUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("{id:guid}/fulfillment/delivered")]
  public async Task<IActionResult> MarkDelivered(Guid id, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkDeliveredAsync(id, adminUserId, now, ip, userAgent, ct);
    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }
}
