using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Contracts.Orders;

public sealed record OrderRealtimeSnapshot(
  Guid OrderId,
  Guid? UserId,
  string OrderNumber,
  string Status,
  string? PaymentStatus,
  string? PaymentProvider,
  DateTimeOffset? PaidAt,
  DateTimeOffset? PaymentDueAt,
  int TotalCents,
  string CurrencyCode,
  string SourceType,
  Guid? AuctionId,
  Guid? FulfillmentGroupId,
  DateTimeOffset UpdatedAt,
  List<OrderTimelineEntryDto>? NewTimelineEntries
);