using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
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

  public OpenBoxShippingInvoiceController(
    MineralKingdomDbContext db,
    ShippingInvoicePaymentService payments)
  {
    _db = db;
    _payments = payments;
  }

  [HttpGet]
  public async Task<IActionResult> GetCurrent(CancellationToken ct)
  {
    var userId = User.GetUserId();

    var currentGroup = await (
      from g in _db.FulfillmentGroups.AsNoTracking()
      where g.UserId == userId
      where _db.Orders.Any(o =>
        o.FulfillmentGroupId == g.Id &&
        o.UserId == userId &&
        o.ShippingMode == StoreShippingModes.OpenBox)
      orderby
        g.BoxStatus == "OPEN" ? 0 :
        g.BoxStatus == "LOCKED_FOR_REVIEW" ? 1 :
        g.BoxStatus == "CLOSED" ? 2 :
        g.BoxStatus == "SHIPPED" ? 3 : 4,
        g.UpdatedAt descending
      select g
    ).FirstOrDefaultAsync(ct);

    if (currentGroup is null)
      return NotFound(new { error = "OPEN_BOX_NOT_FOUND" });

    var inv = await _db.ShippingInvoices.AsNoTracking()
      .Where(i => i.FulfillmentGroupId == currentGroup.Id)
      .OrderByDescending(i => i.CreatedAt)
      .FirstOrDefaultAsync(ct);

    if (inv is null)
      return NotFound(new { error = "INVOICE_NOT_FOUND" });

    var (ok, err, detail) = await _payments.GetInvoiceDetailForUserAsync(userId, inv.Id, ct);

    if (!ok || detail is null)
      return NotFound(new { error = err ?? "INVOICE_NOT_FOUND" });

    return Ok(detail);
  }

  [HttpPost("pay")]
  public async Task<IActionResult> Pay([FromBody] CreateShippingInvoicePaymentRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var currentGroup = await (
      from g in _db.FulfillmentGroups.AsNoTracking()
      where g.UserId == userId
      where _db.Orders.Any(o =>
        o.FulfillmentGroupId == g.Id &&
        o.UserId == userId &&
        o.ShippingMode == StoreShippingModes.OpenBox)
      orderby
        g.BoxStatus == "OPEN" ? 0 :
        g.BoxStatus == "LOCKED_FOR_REVIEW" ? 1 :
        g.BoxStatus == "CLOSED" ? 2 :
        g.BoxStatus == "SHIPPED" ? 3 : 4,
        g.UpdatedAt descending
      select g
    ).FirstOrDefaultAsync(ct);

    if (currentGroup is null)
      return BadRequest(new { error = "OPEN_BOX_NOT_FOUND" });

    var (ok, err, result) = await _payments.StartAsync(
      userId,
      currentGroup.Id,
      req.Provider,
      req.SuccessUrl,
      req.CancelUrl,
      now,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(result);
  }
}