using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/shipping-invoices")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminShippingInvoicesController : ControllerBase
{
  private readonly ShippingInvoiceService _shipping;
  private readonly MineralKingdomDbContext _db;

  public AdminShippingInvoicesController(
    ShippingInvoiceService shipping,
    MineralKingdomDbContext db)
  {
    _shipping = shipping;
    _db = db;
  }

  [HttpGet("{id:guid}")]
  public async Task<IActionResult> Get(Guid id, CancellationToken ct)
  {
    var invoice = await _db.ShippingInvoices.AsNoTracking()
      .Where(i => i.Id == id)
      .Select(i => new
      {
        shippingInvoiceId = i.Id,
        fulfillmentGroupId = i.FulfillmentGroupId,
        amountCents = i.AmountCents,
        calculatedAmountCents = i.CalculatedAmountCents,
        currencyCode = i.CurrencyCode,
        status = i.Status,
        paidAt = i.PaidAt,
        createdAt = i.CreatedAt,
        updatedAt = i.UpdatedAt,
        isOverride = i.IsOverride,
        overrideReason = i.OverrideReason,
        provider = i.Provider,
        providerCheckoutId = i.ProviderCheckoutId,
        providerPaymentId = i.ProviderPaymentId,
        paymentReference = i.PaymentReference
      })
      .SingleOrDefaultAsync(ct);

    if (invoice is null)
      return NotFound(new { error = "SHIPPING_INVOICE_NOT_FOUND" });

    return Ok(invoice);
  }

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