using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/me/open-box/shipping-invoice")]
[Authorize(Policy = AuthorizationPolicies.EmailVerified)]
public sealed class OpenBoxShippingInvoiceController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingInvoicePaymentService _payments;

  public OpenBoxShippingInvoiceController(MineralKingdomDbContext db, ShippingInvoicePaymentService payments)
  {
    _db = db;
    _payments = payments;
  }

  [HttpGet]
  public async Task<IActionResult> GetCurrent(CancellationToken ct)
  {
    var userId = User.GetUserId();

    var group = await _db.FulfillmentGroups.AsNoTracking()
      .OrderByDescending(g => g.UpdatedAt)
      .FirstOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "CLOSED", ct);

    if (group is null) return NotFound(new { error = "NO_CLOSED_BOX" });

    var inv = await _db.ShippingInvoices.AsNoTracking()
      .OrderByDescending(i => i.CreatedAt)
      .FirstOrDefaultAsync(i => i.FulfillmentGroupId == group.Id, ct);

    if (inv is null) return NotFound(new { error = "INVOICE_NOT_FOUND" });

    return Ok(new
    {
      shippingInvoiceId = inv.Id,
      fulfillmentGroupId = group.Id,
      amountCents = inv.AmountCents,
      currencyCode = inv.CurrencyCode,
      status = inv.Status,
      provider = inv.Provider,
      providerCheckoutId = inv.ProviderCheckoutId
    });
  }

  [HttpPost("pay")]
  public async Task<IActionResult> Pay([FromBody] CreateShippingInvoicePaymentRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var group = await _db.FulfillmentGroups.AsNoTracking()
      .OrderByDescending(g => g.UpdatedAt)
      .FirstOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "CLOSED", ct);

    if (group is null) return BadRequest(new { error = "NO_CLOSED_BOX" });

    var (ok, err, result) = await _payments.StartAsync(
      userId,
      group.Id,
      req.Provider,
      req.SuccessUrl,
      req.CancelUrl,
      now,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(result);
  }
}