namespace MineralKingdom.Contracts.Orders;

public sealed record OpenBoxDto(
  Guid FulfillmentGroupId,
  string BoxStatus,
  string ShipmentRequestStatus,
  string FulfillmentStatus,
  DateTimeOffset? ClosedAt,
  int OrderCount,
  IReadOnlyList<OpenBoxOrderDto> Orders
);

public sealed record OpenBoxOrderDto(
  Guid OrderId,
  string OrderNumber,
  long TotalCents,
  string CurrencyCode,
  string Status
);