using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Orders;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
public sealed class AdminRefundsController : ControllerBase
{
  private readonly OrderRefundService _refunds;

  public AdminRefundsController(OrderRefundService refunds) => _refunds = refunds;

  [HttpPost("{id:guid}/refunds")]
  public async Task<IActionResult> CreateRefund(Guid id, [FromBody] AdminCreateRefundRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var now = DateTimeOffset.UtcNow;
    var actorUserId = User.GetUserId();

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err, refund) = await _refunds.AdminCreateRefundAsync(
      id,
      req.AmountCents,
      req.Reason,
      req.Provider,
      actorUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(new
    {
      refund!.Id,
      refund.OrderId,
      refund.AmountCents,
      refund.CurrencyCode,
      refund.Provider,
      refund.ProviderRefundId,
      refund.Reason,
      refund.CreatedAt
    });
  }
}