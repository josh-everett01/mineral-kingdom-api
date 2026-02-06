using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Payments;
using Stripe;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class PaymentWebhooksController : ControllerBase
{
  private readonly PaymentWebhookService _svc;

  public PaymentWebhooksController(PaymentWebhookService svc)
  {
    _svc = svc;
  }

  [HttpPost("stripe")]
  public async Task<IActionResult> Stripe(
    [FromServices] IHostEnvironment env,
    [FromServices] IOptions<StripeOptions> stripe,
    CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    using var reader = new StreamReader(Request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync(ct);

    // In tests we allow simplified ingestion (no signature verification required)
    if (env.IsEnvironment("Testing"))
    {
      var eventId = Request.Headers["X-Stripe-Event-Id"].ToString();
      if (string.IsNullOrWhiteSpace(eventId))
        eventId = Guid.NewGuid().ToString("N");

      await _svc.ProcessStripeAsync(eventId, body, now, ct);
      return Ok();
    }

    // Non-testing: verify signature
    var webhookSecret = stripe.Value.WebhookSecret;
    if (string.IsNullOrWhiteSpace(webhookSecret))
      return BadRequest(new { error = "STRIPE_WEBHOOK_SECRET_NOT_CONFIGURED" });

    if (!Request.Headers.TryGetValue("Stripe-Signature", out var sig) || string.IsNullOrWhiteSpace(sig))
      return BadRequest(new { error = "STRIPE_SIGNATURE_MISSING" });

    try
    {
      var evt = EventUtility.ConstructEvent(body, sig!, webhookSecret, throwOnApiVersionMismatch: false);
      await _svc.ProcessStripeAsync(evt.Id, body, now, ct);
      return Ok();
    }
    catch (StripeException ex)
    {
      Console.WriteLine($"Stripe webhook error: {ex.Message}");
      return BadRequest(new { error = "STRIPE_WEBHOOK_INVALID", message = ex.Message });
    }
  }

  [HttpPost("paypal")]
  public async Task<IActionResult> PayPal(CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    using var reader = new StreamReader(Request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync(ct);

    // Prefer transmission id if present; otherwise fall back to generated.
    var eventId = Request.Headers["PAYPAL-TRANSMISSION-ID"].ToString();
    if (string.IsNullOrWhiteSpace(eventId))
      eventId = Guid.NewGuid().ToString("N");

    await _svc.ProcessPayPalAsync(eventId, body, now, ct);
    return Ok();
  }
}
