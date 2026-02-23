namespace MineralKingdom.Contracts.Orders;

public sealed record OpenBoxDto(
  Guid FulfillmentGroupId,
  string BoxStatus,
  string FulfillmentStatus,
  DateTimeOffset? ClosedAt,
  int OrderCount,
  List<OpenBoxOrderDto> Orders);

public sealed record OpenBoxOrderDto(
  Guid OrderId,
  string OrderNumber,
  int TotalCents,
  string CurrencyCode,
  string Status);