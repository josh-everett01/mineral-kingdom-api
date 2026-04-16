using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Orders.Realtime;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class FulfillmentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingInvoiceService _shipping;
  private readonly EmailOutboxService _emails;
  private readonly IFulfillmentRealtimePublisher _realtime;

  public FulfillmentService(
    MineralKingdomDbContext db,
    ShippingInvoiceService shipping,
    EmailOutboxService emails,
    IFulfillmentRealtimePublisher realtime)
  {
    _db = db;
    _shipping = shipping;
    _emails = emails;
    _realtime = realtime;
  }

  public async Task<(bool Ok, string? Error)> AdminMarkPackedAsync(
    Guid groupId,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    var orders = await _db.Orders
      .Where(o => o.FulfillmentGroupId == groupId)
      .ToListAsync(ct);

    if (orders.Count == 0) return (false, "GROUP_HAS_NO_ORDERS");

    if (orders.Any(o => !string.Equals(o.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase)))
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    if (string.Equals(group.Status, "PACKED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
      return (false, "INVALID_FULFILLMENT_TRANSITION");

    var before = Snapshot(group);

    group.Status = "PACKED";
    group.PackedAt ??= now;
    group.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "FULFILLMENT_GROUP_PACKED",
      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,
      BeforeJson = before,
      AfterJson = Snapshot(group),
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);

    try { await _realtime.PublishFulfillmentAsync(group.Id, now, ct); } catch { }

    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> AdminMarkShippedAsync(
  Guid groupId,
  string shippingCarrier,
  string trackingNumber,
  Guid actorUserId,
  DateTimeOffset now,
  string? ipAddress,
  string? userAgent,
  CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    var orders = await _db.Orders
      .Where(o => o.FulfillmentGroupId == groupId)
      .ToListAsync(ct);

    if (orders.Count == 0) return (false, "GROUP_HAS_NO_ORDERS");

    if (orders.Any(o => !string.Equals(o.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase)))
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    if (string.IsNullOrWhiteSpace(shippingCarrier)) return (false, "CARRIER_REQUIRED");
    if (string.IsNullOrWhiteSpace(trackingNumber)) return (false, "TRACKING_REQUIRED");
    if (shippingCarrier.Length > 64) return (false, "CARRIER_TOO_LONG");
    if (trackingNumber.Length > 128) return (false, "TRACKING_TOO_LONG");

    if (string.Equals(group.Status, "SHIPPED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(group.BoxStatus, "LOCKED_FOR_REVIEW", StringComparison.OrdinalIgnoreCase))
      return (false, "BOX_NOT_CLOSED");

    var requiresShippingInvoice = orders.Any(o =>
      string.Equals(o.ShippingMode, "OPEN_BOX", StringComparison.OrdinalIgnoreCase));

    if (requiresShippingInvoice)
    {
      var (invOk, invErr, inv) = await _shipping.EnsureInvoiceForGroupAsync(group.Id, now, ct);
      if (!invOk || inv is null) return (false, invErr);

      if (inv.AmountCents > 0 && !string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
        return (false, "SHIPPING_UNPAID");
    }

    if (!string.Equals(group.Status, "PACKED", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_PACKED");

    var before = Snapshot(group);

    group.Status = "SHIPPED";
    group.ShippedAt ??= now;
    group.ShippingCarrier = shippingCarrier.Trim();
    group.TrackingNumber = trackingNumber.Trim();
    group.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "FULFILLMENT_GROUP_SHIPPED",
      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,
      BeforeJson = before,
      AfterJson = Snapshot(group),
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);

    try { await _realtime.PublishFulfillmentAsync(group.Id, now, ct); } catch { }

    try
    {
      if (group.UserId is Guid uid)
      {
        var toEmail = await _db.Users.AsNoTracking()
          .Where(u => u.Id == uid)
          .Select(u => u.Email)
          .SingleOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
          var payload =
            $"{{\"groupId\":\"{group.Id}\",\"carrier\":\"{group.ShippingCarrier}\",\"tracking\":\"{group.TrackingNumber}\"}}";

          await _emails.EnqueueAsync(
            toEmail: toEmail,
            templateKey: EmailTemplateKeys.ShipmentConfirmed,
            payloadJson: payload,
            dedupeKey: EmailDedupeKeys.ShipmentConfirmed(group.Id, toEmail),
            now: now,
            ct: ct);
        }
      }
    }
    catch
    {
      // best-effort
    }

    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> AdminMarkDeliveredAsync(
    Guid groupId,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    var ordersExist = await _db.Orders.AnyAsync(o => o.FulfillmentGroupId == groupId, ct);
    if (!ordersExist) return (false, "GROUP_HAS_NO_ORDERS");

    if (string.Equals(group.Status, "DELIVERED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.Status, "SHIPPED", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_SHIPPED");

    var before = Snapshot(group);

    group.Status = "DELIVERED";
    group.DeliveredAt ??= now;
    group.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "FULFILLMENT_GROUP_DELIVERED",
      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,
      BeforeJson = before,
      AfterJson = Snapshot(group),
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);

    try { await _realtime.PublishFulfillmentAsync(group.Id, now, ct); } catch { }

    return (true, null);
  }

  private static string Snapshot(FulfillmentGroup g)
  {
    var payload = new
    {
      g.Id,
      g.Status,
      g.BoxStatus,
      g.PackedAt,
      g.ShippedAt,
      g.DeliveredAt,
      g.ShippingCarrier,
      g.TrackingNumber,
      g.UpdatedAt
    };

    return JsonSerializer.Serialize(payload);
  }
}