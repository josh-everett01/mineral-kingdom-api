using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Payments;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/shipping-invoices")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminShippingInvoicesController : ControllerBase
{
  private readonly ShippingInvoiceService _shipping;

  public AdminShippingInvoicesController(ShippingInvoiceService shipping) => _shipping = shipping;

  [HttpPost("{id:guid}/override")]
  public async Task<IActionResult> Override(Guid id, [FromBody] AdminOverrideShippingInvoiceRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var now = DateTimeOffset.UtcNow;
    var actorUserId = User.GetUserId();

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _shipping.AdminOverrideInvoiceAsync(
      id,
      req.AmountCents,
      req.Reason,
      actorUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });
    return NoContent();
  }
}