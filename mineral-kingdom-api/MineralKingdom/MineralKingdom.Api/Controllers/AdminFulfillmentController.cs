using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/fulfillment")]
[Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
public sealed class AdminFulfillmentController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly AdminFulfillmentWorkflowService _workflow;
  private readonly ShippingInvoiceService _shipping;
  private readonly FulfillmentService _fulfillment;

  public AdminFulfillmentController(
    MineralKingdomDbContext db,
    AdminFulfillmentWorkflowService workflow,
    ShippingInvoiceService shipping,
    FulfillmentService fulfillment)
  {
    _db = db;
    _workflow = workflow;
    _shipping = shipping;
    _fulfillment = fulfillment;
  }

  [HttpGet("open-boxes")]
  public async Task<IActionResult> ListOpenBoxes(CancellationToken ct)
  {
    var groups = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g =>
        g.BoxStatus == "OPEN" ||
        g.BoxStatus == "LOCKED_FOR_REVIEW" ||
        g.BoxStatus == "CLOSED" ||
        g.Status == "READY_TO_FULFILL" ||
        g.Status == "PACKED" ||
        g.Status == "SHIPPED")
      .OrderByDescending(g => g.UpdatedAt)
      .Select(g => new
      {
        g.Id,
        g.UserId,
        g.BoxStatus,
        g.ShipmentRequestStatus,
        g.ShipmentRequestedAt,
        g.ShipmentReviewedAt,
        g.ShipmentReviewedByUserId,
        FulfillmentStatus = g.Status,
        g.CreatedAt,
        g.UpdatedAt
      })
      .ToListAsync(ct);

    var ids = groups.Select(g => g.Id).ToList();

    var counts = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId != null && ids.Contains(o.FulfillmentGroupId.Value))
      .GroupBy(o => o.FulfillmentGroupId!.Value)
      .Select(g => new
      {
        groupId = g.Key,
        count = g.Count()
      })
      .ToListAsync(ct);

    var countById = counts.ToDictionary(x => x.groupId, x => x.count);

    var latestInvoices = await _db.ShippingInvoices.AsNoTracking()
      .Where(i => ids.Contains(i.FulfillmentGroupId))
      .GroupBy(i => i.FulfillmentGroupId)
      .Select(g => g
        .OrderByDescending(x => x.CreatedAt)
        .Select(x => new
        {
          x.FulfillmentGroupId,
          shippingInvoiceId = x.Id,
          shippingInvoiceStatus = x.Status,
          shippingInvoicePaidAt = x.PaidAt
        })
        .FirstOrDefault())
      .ToListAsync(ct);

    var invoiceByGroupId = latestInvoices
      .Where(x => x != null)
      .ToDictionary(x => x!.FulfillmentGroupId, x => x);

    static string DeriveQueueState(
  string shipmentRequestStatus,
  string fulfillmentStatus,
  string? shippingInvoiceStatus)
    {
      var shipment = (shipmentRequestStatus ?? "").Trim().ToUpperInvariant();
      var fulfillment = (fulfillmentStatus ?? "").Trim().ToUpperInvariant();
      var invoice = (shippingInvoiceStatus ?? "").Trim().ToUpperInvariant();

      if (shipment == "NONE" && fulfillment == "READY_TO_FULFILL") return "DIRECT_READY";
      if (shipment == "NONE" && fulfillment == "PACKED") return "DIRECT_PACKED";
      if (shipment == "NONE" && fulfillment == "SHIPPED") return "DIRECT_SHIPPED";
      if (shipment == "NONE" && fulfillment == "DELIVERED") return "DIRECT_DELIVERED";

      if (invoice == "PAID") return "SHIPPING_PAID";
      if (fulfillment == "SHIPPED") return "SHIPPED";
      if (fulfillment == "PACKED") return "PACKED";
      if (shipment == "INVOICED") return "INVOICED_AWAITING_PAYMENT";
      if (shipment == "UNDER_REVIEW") return "UNDER_REVIEW";
      if (shipment == "REQUESTED") return "REQUESTED";

      return "OTHER";
    }

    var result = groups.Select(g =>
    {
      invoiceByGroupId.TryGetValue(g.Id, out var invoice);

      return new
      {
        fulfillmentGroupId = g.Id,
        userId = g.UserId,
        boxStatus = g.BoxStatus,
        shipmentRequestStatus = g.ShipmentRequestStatus,
        shipmentRequestedAt = g.ShipmentRequestedAt,
        shipmentReviewedAt = g.ShipmentReviewedAt,
        shipmentReviewedByUserId = g.ShipmentReviewedByUserId,
        fulfillmentStatus = g.FulfillmentStatus,
        createdAt = g.CreatedAt,
        updatedAt = g.UpdatedAt,
        orderCount = countById.TryGetValue(g.Id, out var c) ? c : 0,

        shippingInvoiceId = invoice?.shippingInvoiceId,
        shippingInvoiceStatus = invoice?.shippingInvoiceStatus,
        shippingInvoicePaidAt = invoice?.shippingInvoicePaidAt,

        queueState = DeriveQueueState(
          g.ShipmentRequestStatus,
          g.FulfillmentStatus,
          invoice?.shippingInvoiceStatus)
      };
    });

    return Ok(result);
  }

  [HttpGet("groups/{groupId:guid}")]
  public async Task<ActionResult<object>> GetGroup(Guid groupId, CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups.AsNoTracking()
      .SingleOrDefaultAsync(g => g.Id == groupId, ct);

    if (box is null) return NotFound(new { error = "GROUP_NOT_FOUND" });

    var orders = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId == groupId)
      .OrderBy(o => o.CreatedAt)
      .Select(o => new
      {
        orderId = o.Id,
        orderNumber = o.OrderNumber,
        totalCents = o.TotalCents,
        currencyCode = o.CurrencyCode,
        status = o.Status,
        shippingMode = o.ShippingMode
      })
      .ToListAsync(ct);

    var invoice = await _db.ShippingInvoices.AsNoTracking()
      .Where(i => i.FulfillmentGroupId == groupId)
      .OrderByDescending(i => i.CreatedAt)
      .Select(i => new
      {
        shippingInvoiceId = i.Id,
        amountCents = i.AmountCents,
        currencyCode = i.CurrencyCode,
        status = i.Status,
        paidAt = i.PaidAt,
        createdAt = i.CreatedAt,
        updatedAt = i.UpdatedAt,
        calculatedAmountCents = i.CalculatedAmountCents,
        isOverride = i.IsOverride,
        overrideReason = i.OverrideReason
      })
      .FirstOrDefaultAsync(ct);

    var calculatedShippingCents = await _shipping.CalculateTierShippingCentsAsync(groupId, ct);
    var currencyCode = orders.Select(o => o.currencyCode).FirstOrDefault() ?? "USD";

    var requiresShippingInvoice = orders.Any(o =>
      string.Equals(o.shippingMode, "OPEN_BOX", StringComparison.OrdinalIgnoreCase));

    return Ok(new
    {
      fulfillmentGroupId = box.Id,
      boxStatus = box.BoxStatus,
      shipmentRequestStatus = box.ShipmentRequestStatus,
      shipmentRequestedAt = box.ShipmentRequestedAt,
      shipmentReviewedAt = box.ShipmentReviewedAt,
      shipmentReviewedByUserId = box.ShipmentReviewedByUserId,
      fulfillmentStatus = box.Status,
      closedAt = box.ClosedAt,
      orderCount = orders.Count,
      orders,
      shippingInvoice = invoice,
      calculatedShippingCents,
      currencyCode,
      requiresShippingInvoice
    });
  }

  [HttpPost("groups/{groupId:guid}/shipping-invoice")]
  public async Task<IActionResult> CreateShippingInvoice(
    Guid groupId,
    [FromBody] AdminCreateShippingInvoiceRequest? req,
    CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err, invoice) = await _workflow.AdminCreateShippingInvoiceForGroupAsync(
      groupId,
      adminUserId,
      req?.AmountCents,
      req?.Reason,
      now,
      ip,
      userAgent,
      ct);

    if (!ok || invoice is null)
      return BadRequest(new { error = err });

    return Ok(new
    {
      shippingInvoiceId = invoice.Id,
      fulfillmentGroupId = invoice.FulfillmentGroupId,
      amountCents = invoice.AmountCents,
      currencyCode = invoice.CurrencyCode,
      status = invoice.Status,
      paidAt = invoice.PaidAt,
      createdAt = invoice.CreatedAt,
      updatedAt = invoice.UpdatedAt,
      calculatedAmountCents = invoice.CalculatedAmountCents,
      isOverride = invoice.IsOverride,
      overrideReason = invoice.OverrideReason
    });
  }

  [HttpPost("groups/{groupId:guid}/packed")]
  public async Task<IActionResult> MarkPacked(Guid groupId, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkPackedAsync(
      groupId,
      adminUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("groups/{groupId:guid}/shipped")]
  public async Task<IActionResult> MarkShipped(
    Guid groupId,
    [FromBody] AdminMarkShippedRequest req,
    CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkShippedAsync(
      groupId,
      req.ShippingCarrier,
      req.TrackingNumber,
      adminUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("groups/{groupId:guid}/delivered")]
  public async Task<IActionResult> MarkDelivered(Guid groupId, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = Request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

    var (ok, err) = await _fulfillment.AdminMarkDeliveredAsync(
      groupId,
      adminUserId,
      now,
      ip,
      userAgent,
      ct);

    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }
}