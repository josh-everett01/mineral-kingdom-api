using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Payments;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
  private readonly CheckoutPaymentService _payments;

  public PaymentsController(CheckoutPaymentService payments)
  {
    _payments = payments;
  }

  [HttpPost("start")]
  [AllowAnonymous]
  public async Task<ActionResult<StartPaymentResponse>> Start([FromBody] StartPaymentRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var (ok, err, payment, redirectUrl) = await _payments.StartAsync(
      req.HoldId,
      req.Provider,
      req.SuccessUrl,
      req.CancelUrl,
      now,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(new StartPaymentResponse(payment!.Id, payment.Provider, redirectUrl!));
  }

  [HttpPost("{id:guid}/capture")]
  [AllowAnonymous]
  public async Task<ActionResult<CapturePaymentResponse>> Capture([FromRoute] Guid id, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var (ok, err, payment) = await _payments.CaptureAsync(id, now, ct);
    if (!ok)
    {
      return err switch
      {
        "PAYMENT_NOT_FOUND" => NotFound(new { error = err }),
        _ => BadRequest(new { error = err })
      };
    }

    return Ok(new CapturePaymentResponse(
      payment!.Id,
      payment.Provider,
      payment.Status,
      payment.ProviderPaymentId));
  }

  [HttpGet("{id:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<PaymentStatusResponse>> Get([FromRoute] Guid id, CancellationToken ct)
  {
    var pay = await _payments.GetAsync(id, ct);
    if (pay is null) return NotFound();

    return Ok(new PaymentStatusResponse(pay.Id, pay.Provider, pay.Status));
  }

  [HttpGet("{id:guid}/confirmation")]
  [AllowAnonymous]
  public async Task<ActionResult<PaymentConfirmationResponse>> GetConfirmation([FromRoute] Guid id, CancellationToken ct)
  {
    var dto = await _payments.GetConfirmationAsync(id, ct);
    if (dto is null) return NotFound(new { error = "PAYMENT_NOT_FOUND" });

    return Ok(dto);
  }
}