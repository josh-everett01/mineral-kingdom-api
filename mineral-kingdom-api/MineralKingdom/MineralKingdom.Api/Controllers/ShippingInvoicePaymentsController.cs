using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Payments;

namespace MineralKingdom.Api.Controllers;

[ApiController]
public sealed class ShippingInvoicePaymentsController : ControllerBase
{
  private readonly ShippingInvoicePaymentService _svc;

  public ShippingInvoicePaymentsController(ShippingInvoicePaymentService svc)
  {
    _svc = svc;
  }

  [HttpPost("/api/shipping-invoices/{invoiceId:guid}/payments/capture")]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
  public async Task<ActionResult<CaptureShippingInvoicePaymentResponse>> Capture(
    Guid invoiceId,
    CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err, invoice) = await _svc.CaptureAsync(invoiceId, userId, now, ct);

    if (!ok || invoice is null)
    {
      return err switch
      {
        "INVOICE_NOT_FOUND" => NotFound(new { error = "INVOICE_NOT_FOUND" }),
        "PROVIDER_CAPTURE_NOT_SUPPORTED" => BadRequest(new { error = "PROVIDER_CAPTURE_NOT_SUPPORTED" }),
        "PROVIDER_CHECKOUT_ID_MISSING" => Conflict(new { error = "PROVIDER_CHECKOUT_ID_MISSING" }),
        "PAYPAL_CAPTURE_FAILED" => BadRequest(new { error = "PAYPAL_CAPTURE_FAILED" }),
        _ => BadRequest(new { error = err ?? "CAPTURE_FAILED" })
      };
    }

    return Ok(new CaptureShippingInvoicePaymentResponse(
      ShippingInvoiceId: invoice.Id,
      Provider: invoice.Provider ?? PaymentProviders.PayPal,
      PaymentStatus: invoice.Status,
      ProviderPaymentId: invoice.ProviderPaymentId
    ));
  }
}