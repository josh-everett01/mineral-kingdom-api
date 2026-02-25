namespace MineralKingdom.Contracts.Admin.Queues;

public sealed record AdminQueueFulfillmentGroupDto(
  Guid FulfillmentGroupId,
  string BoxStatus,
  string Status,
  string? ShippingCarrier,
  string? TrackingNumber,
  DateTimeOffset UpdatedAt,
  int OrderCount);