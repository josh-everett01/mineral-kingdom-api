namespace MineralKingdom.Contracts.Admin.Queues;

public sealed record AdminQueueOrderDto(
  Guid OrderId,
  string OrderNumber,
  string SourceType,
  string Status,
  int TotalCents,
  string CurrencyCode,
  DateTimeOffset CreatedAt,
  DateTimeOffset? PaymentDueAt,
  Guid? FulfillmentGroupId);