using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Payments;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/shipping-invoices")]
[Authorize(Policy = AuthorizationPolicies.EmailVerified)]
public sealed class ShippingInvoicesController : ControllerBase
{
  private readonly ShippingInvoicePaymentService _payments;

  public ShippingInvoicesController(ShippingInvoicePaymentService payments)
  {
    _payments = payments;
  }

  [HttpGet("{invoiceId:guid}")]
  public async Task<IActionResult> GetById(Guid invoiceId, CancellationToken ct)
  {
    var userId = User.GetUserId();

    var (ok, err, detail) = await _payments.GetInvoiceDetailForUserAsync(userId, invoiceId, ct);

    if (!ok || detail is null)
    {
      return err switch
      {
        "INVOICE_NOT_FOUND" => NotFound(new { error = "INVOICE_NOT_FOUND" }),
        "FORBIDDEN" => NotFound(new { error = "INVOICE_NOT_FOUND" }),
        _ => NotFound(new { error = err ?? "INVOICE_NOT_FOUND" })
      };
    }

    return Ok(detail);
  }

  [HttpPost("{invoiceId:guid}/pay")]
  public async Task<IActionResult> Pay(
    Guid invoiceId,
    [FromBody] CreateShippingInvoicePaymentRequest req,
    CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err, result) = await _payments.StartForInvoiceAsync(
      invoiceId,
      userId,
      req.Provider,
      req.SuccessUrl,
      req.CancelUrl,
      now,
      ct);

    if (!ok || result is null)
    {
      return err switch
      {
        "INVOICE_NOT_FOUND" => NotFound(new { error = "INVOICE_NOT_FOUND" }),
        "FORBIDDEN" => NotFound(new { error = "INVOICE_NOT_FOUND" }),
        "INVOICE_ALREADY_PAID" => Conflict(new { error = "INVOICE_ALREADY_PAID" }),
        "INVALID_INVOICE_STATUS" => Conflict(new { error = "INVALID_INVOICE_STATUS" }),
        _ => BadRequest(new { error = err ?? "PAYMENT_START_FAILED" })
      };
    }

    return Ok(result);
  }
}