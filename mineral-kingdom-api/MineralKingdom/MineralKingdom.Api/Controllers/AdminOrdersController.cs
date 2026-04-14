using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
  private readonly OrderService _orders;
  private readonly FulfillmentService _fulfillment;
  private readonly AdminOrderQueryService _adminOrders;

  public AdminOrdersController(
    OrderService orders,
    FulfillmentService fulfillment,
    AdminOrderQueryService adminOrders)
  {
    _orders = orders;
    _fulfillment = fulfillment;
    _adminOrders = adminOrders;
  }

  [HttpGet]
  [Authorize(Policy = AuthorizationPolicies.AdminAccess)]
  public async Task<ActionResult<AdminOrdersResponseDto>> List(
    [FromQuery] string? status,
    [FromQuery] string? q,
    CancellationToken ct)
  {
    var dto = await _adminOrders.GetAdminOrdersAsync(status, q, ct);
    return Ok(dto);
  }

  [HttpGet("{id:guid}")]
  [Authorize(Policy = AuthorizationPolicies.AdminAccess)]
  public async Task<ActionResult<AdminOrderDetailDto>> Get(Guid id, CancellationToken ct)
  {
    var canRefund = User.IsInRole(UserRoles.Owner);
    var dto = await _adminOrders.GetAdminOrderDetailAsync(id, canRefund, ct);

    if (dto is null)
      return NotFound();

    return Ok(dto);
  }

  [HttpPost("{id:guid}/payment-due")]
  [Authorize(Roles = UserRoles.Owner)]
  public async Task<IActionResult> ExtendPaymentDue(Guid id, [FromBody] ExtendPaymentDueRequest req, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });
    if (req.PaymentDueAt <= now) return BadRequest(new { error = "PAYMENT_DUE_MUST_BE_IN_FUTURE" });

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
  [Authorize(Roles = UserRoles.Owner)]
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
  [Authorize(Roles = UserRoles.Owner)]
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
  [Authorize(Roles = UserRoles.Owner)]
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