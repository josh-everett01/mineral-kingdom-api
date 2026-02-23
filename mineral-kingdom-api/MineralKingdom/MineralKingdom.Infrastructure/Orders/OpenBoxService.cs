using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class OpenBoxService
{
  private readonly MineralKingdomDbContext _db;

  private readonly ShippingInvoiceService _shipping;

  public OpenBoxService(MineralKingdomDbContext db, ShippingInvoiceService shipping)
  {
    _db = db;
    _shipping = shipping;
  }

  public async Task<(bool Ok, string? Error, FulfillmentGroup? Box)> GetOrCreateOpenBoxAsync(
    Guid userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    // One open box per user
    var existing = await _db.FulfillmentGroups
      .SingleOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "OPEN", ct);

    if (existing is not null) return (true, null, existing);

    var box = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      GuestEmail = null,

      BoxStatus = "OPEN",
      ClosedAt = null,

      // Fulfillment status remains READY_TO_FULFILL while open
      Status = "READY_TO_FULFILL",

      CreatedAt = now,
      UpdatedAt = now
    };

    _db.FulfillmentGroups.Add(box);
    await _db.SaveChangesAsync(ct);

    return (true, null, box);
  }

  public async Task<(bool Ok, string? Error)> AddOrderToOpenBoxAsync(
    Guid userId,
    Guid orderId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups
      .SingleOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "OPEN", ct);

    if (box is null) return (false, "NO_OPEN_BOX");
    if (!string.Equals(box.BoxStatus, "OPEN", StringComparison.OrdinalIgnoreCase)) return (false, "BOX_CLOSED");

    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");
    if (order.UserId != userId) return (false, "FORBIDDEN");

    // Only paid/ready orders can be assigned
    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    // Already in this box => idempotent
    if (order.FulfillmentGroupId == box.Id) return (true, null);

    // If assigned elsewhere, only allow moving from a single-order, open, READY group
    if (order.FulfillmentGroupId is Guid existingGroupId && existingGroupId != box.Id)
    {
      var existingGroup = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == existingGroupId, ct);
      if (existingGroup is null) return (false, "ORDER_GROUP_MISSING");

      if (!string.Equals(existingGroup.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
        return (false, "ORDER_ALREADY_IN_FULFILLMENT");

      if (string.Equals(existingGroup.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
        return (false, "ORDER_GROUP_CLOSED");

      var count = await _db.Orders.CountAsync(o => o.FulfillmentGroupId == existingGroupId, ct);
      if (count > 1) return (false, "ORDER_GROUP_HAS_MULTIPLE_ORDERS");
    }

    order.FulfillmentGroupId = box.Id;
    order.UpdatedAt = now;

    box.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> RemoveOrderFromOpenBoxAsync(
    Guid userId,
    Guid orderId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups
      .SingleOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "OPEN", ct);

    if (box is null) return (false, "NO_OPEN_BOX");

    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");
    if (order.UserId != userId) return (false, "FORBIDDEN");

    if (order.FulfillmentGroupId != box.Id) return (true, null); // idempotent

    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    order.FulfillmentGroupId = null;
    order.UpdatedAt = now;

    box.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> CloseOpenBoxAsync(
    Guid userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups
      .SingleOrDefaultAsync(g => g.UserId == userId && g.BoxStatus == "OPEN", ct);

    if (box is null) return (false, "NO_OPEN_BOX");

    var count = await _db.Orders.CountAsync(o => o.FulfillmentGroupId == box.Id, ct);
    if (count == 0) return (false, "EMPTY_BOX");

    box.BoxStatus = "CLOSED";
    box.ClosedAt = now;
    box.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    // Ensure shipping invoice exists when box is closed (idempotent)
    var (ok, err, _) = await _shipping.EnsureInvoiceForGroupAsync(box.Id, now, ct);
    if (!ok) return (false, err);

    return (true, null);
  }
}