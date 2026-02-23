using System;
using System.Collections.Generic;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class FulfillmentGroup
{
  public Guid Id { get; set; }

  // Identity: allow both member + guest groups (matches Order patterns)
  public Guid? UserId { get; set; }
  public string? GuestEmail { get; set; }

  // Fulfillment lifecycle (Option A)
  // READY_TO_FULFILL | PACKED | SHIPPED | DELIVERED
  public string Status { get; set; } = "READY_TO_FULFILL";

  public DateTimeOffset? PackedAt { get; set; }
  public DateTimeOffset? ShippedAt { get; set; }
  public DateTimeOffset? DeliveredAt { get; set; }

  public string? ShippingCarrier { get; set; }
  public string? TrackingNumber { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<Order> Orders { get; set; } = new();
  public List<ShippingInvoice> ShippingInvoices { get; set; } = new();
}