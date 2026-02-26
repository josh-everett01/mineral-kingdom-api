namespace MineralKingdom.Contracts.Orders;

public sealed record OrderRealtimeSnapshot(
  Guid OrderId,
  Guid? UserId,
  string Status,
  DateTimeOffset? PaidAt,
  DateTimeOffset? PaymentDueAt,
  int TotalCents,
  string CurrencyCode,
  string SourceType,
  Guid? AuctionId,
  Guid? FulfillmentGroupId,
  DateTimeOffset UpdatedAt
);