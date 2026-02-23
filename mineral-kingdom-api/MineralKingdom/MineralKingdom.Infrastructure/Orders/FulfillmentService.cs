using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class FulfillmentService
{
  private readonly MineralKingdomDbContext _db;

  public FulfillmentService(MineralKingdomDbContext db) => _db = db;

  public async Task<(bool Ok, string? Error)> AdminMarkPackedAsync(
    Guid orderId,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");

    // Payment gating: must be paid/ready to fulfill
    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase)) // temporary compatibility
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    var group = await EnsureGroupForOrderAsync(order, now, ct);

    // Idempotency
    if (string.Equals(group.Status, "PACKED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
      return (false, "INVALID_FULFILLMENT_TRANSITION");

    var before = Snapshot(group, order.Id);

    group.Status = "PACKED";
    group.PackedAt ??= now;
    group.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),

      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,

      ActionType = "ORDER_FULFILLMENT_PACKED",

      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,

      BeforeJson = before,
      AfterJson = Snapshot(group, order.Id),

      IpAddress = ipAddress,
      UserAgent = userAgent,

      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> AdminMarkShippedAsync(
    Guid orderId,
    string shippingCarrier,
    string trackingNumber,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");

    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase)) // temporary compatibility
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    if (string.IsNullOrWhiteSpace(shippingCarrier)) return (false, "CARRIER_REQUIRED");
    if (string.IsNullOrWhiteSpace(trackingNumber)) return (false, "TRACKING_REQUIRED");
    if (shippingCarrier.Length > 64) return (false, "CARRIER_TOO_LONG");
    if (trackingNumber.Length > 128) return (false, "TRACKING_TOO_LONG");

    var group = await EnsureGroupForOrderAsync(order, now, ct);

    // Idempotency
    if (string.Equals(group.Status, "SHIPPED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.Status, "PACKED", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_PACKED");

    var before = Snapshot(group, order.Id);

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

      ActionType = "ORDER_FULFILLMENT_SHIPPED",

      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,

      BeforeJson = before,
      AfterJson = Snapshot(group, order.Id),

      IpAddress = ipAddress,
      UserAgent = userAgent,

      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> AdminMarkDeliveredAsync(
    Guid orderId,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");

    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase)) // temporary compatibility
      return (false, "ORDER_NOT_READY_TO_FULFILL");

    var group = await EnsureGroupForOrderAsync(order, now, ct);

    // Idempotency
    if (string.Equals(group.Status, "DELIVERED", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    if (!string.Equals(group.Status, "SHIPPED", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_SHIPPED");

    var before = Snapshot(group, order.Id);

    group.Status = "DELIVERED";
    group.DeliveredAt ??= now;
    group.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),

      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,

      ActionType = "ORDER_FULFILLMENT_DELIVERED",

      EntityType = "FULFILLMENT_GROUP",
      EntityId = group.Id,

      BeforeJson = before,
      AfterJson = Snapshot(group, order.Id),

      IpAddress = ipAddress,
      UserAgent = userAgent,

      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  private async Task<FulfillmentGroup> EnsureGroupForOrderAsync(Order order, DateTimeOffset now, CancellationToken ct)
  {
    if (order.FulfillmentGroupId is Guid groupId)
    {
      var existing = await _db.FulfillmentGroups.SingleAsync(x => x.Id == groupId, ct);
      return existing;
    }

    var group = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = order.UserId,
      GuestEmail = order.GuestEmail,
      Status = "READY_TO_FULFILL",
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.FulfillmentGroups.Add(group);

    order.FulfillmentGroupId = group.Id;
    order.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return group;
  }

  private static string Snapshot(FulfillmentGroup g, Guid orderId)
  {
    var payload = new
    {
      orderId,
      g.Id,
      g.Status,
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