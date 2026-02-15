using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = UserRoles.Owner)]
public sealed class AdminOrdersController : ControllerBase
{
  private readonly OrderService _orders;

  public AdminOrdersController(OrderService orders) => _orders = orders;

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

}
