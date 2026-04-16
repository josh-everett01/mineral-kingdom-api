using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class AdminFulfillmentWorkflowService
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingInvoiceService _shipping;

  public AdminFulfillmentWorkflowService(
    MineralKingdomDbContext db,
    ShippingInvoiceService shipping)
  {
    _db = db;
    _shipping = shipping;
  }

  public async Task<(bool Ok, string? Error, ShippingInvoice? Invoice)> AdminCreateShippingInvoiceForGroupAsync(
    Guid groupId,
    Guid adminUserId,
    long? amountCents,
    string? reason,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups
      .SingleOrDefaultAsync(x => x.Id == groupId, ct);

    if (group is null)
      return (false, "GROUP_NOT_FOUND", null);

    if (!string.Equals(group.BoxStatus, "LOCKED_FOR_REVIEW", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
      return (false, "GROUP_NOT_READY_FOR_INVOICE", null);

    if (!string.Equals(group.ShipmentRequestStatus, ShipmentRequestStatuses.Requested, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(group.ShipmentRequestStatus, ShipmentRequestStatuses.UnderReview, StringComparison.OrdinalIgnoreCase))
      return (false, "SHIPMENT_NOT_REQUESTED", null);

    var orderCount = await _db.Orders.CountAsync(o => o.FulfillmentGroupId == groupId, ct);
    if (orderCount == 0)
      return (false, "EMPTY_GROUP", null);

    if (string.Equals(group.ShipmentRequestStatus, ShipmentRequestStatuses.Requested, StringComparison.OrdinalIgnoreCase))
    {
      group.ShipmentRequestStatus = ShipmentRequestStatuses.UnderReview;
      group.ShipmentReviewedAt = now;
      group.ShipmentReviewedByUserId = adminUserId;
      group.UpdatedAt = now;

      await _db.SaveChangesAsync(ct);
    }

    var (ok, err, invoice) = await _shipping.EnsureInvoiceForGroupAsync(groupId, now, ct);
    if (!ok || invoice is null)
      return (false, err ?? "INVOICE_CREATE_FAILED", null);

    if (amountCents.HasValue)
    {
      var calculated = await _shipping.CalculateTierShippingCentsAsync(groupId, ct);
      var isOverride = amountCents.Value != calculated;

      if (amountCents.Value < 0)
        return (false, "AMOUNT_INVALID", null);

      if (isOverride)
      {
        if (string.IsNullOrWhiteSpace(reason))
          return (false, "REASON_REQUIRED", null);

        if (reason.Trim().Length > 500)
          return (false, "REASON_TOO_LONG", null);
      }

      invoice.AmountCents = amountCents.Value;
      invoice.IsOverride = isOverride;
      invoice.OverrideReason = isOverride ? reason!.Trim() : null;
      invoice.UpdatedAt = now;

      if (invoice.AmountCents == 0)
      {
        invoice.Status = "PAID";
        invoice.PaidAt = now;
      }

      await _db.SaveChangesAsync(ct);
    }

    group.ShipmentRequestStatus = ShipmentRequestStatuses.Invoiced;
    group.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return (true, null, invoice);
  }
}