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

  [HttpGet("{id:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<PaymentStatusResponse>> Get([FromRoute] Guid id, CancellationToken ct)
  {
    var pay = await _payments.GetAsync(id, ct);
    if (pay is null) return NotFound();

    return Ok(new PaymentStatusResponse(pay.Id, pay.Provider, pay.Status));
  }
}
