namespace MineralKingdom.Contracts.Orders;

public sealed record FulfillmentRealtimeSnapshot(
  Guid FulfillmentGroupId,
  Guid? UserId,
  string Status,
  string BoxStatus,
  DateTimeOffset? PackedAt,
  DateTimeOffset? ShippedAt,
  DateTimeOffset? DeliveredAt,
  string? ShippingCarrier,
  string? TrackingNumber,
  DateTimeOffset UpdatedAt
);