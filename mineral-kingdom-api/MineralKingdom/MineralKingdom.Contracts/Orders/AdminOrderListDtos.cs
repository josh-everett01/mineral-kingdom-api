namespace MineralKingdom.Contracts.Orders;

public sealed record AdminOrdersResponseDto(
  List<AdminOrderListItemDto> Items,
  int Total
);

public sealed record AdminOrderListItemDto(
  Guid Id,
  string OrderNumber,
  string Status,
  string SourceType,
  string? CustomerEmail,
  string CurrencyCode,
  int SubtotalCents,
  int DiscountTotalCents,
  int ShippingAmountCents,
  int TotalCents,
  DateTimeOffset? PaymentDueAt,
  DateTimeOffset? PaidAt,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  long TotalRefundedCents,
  long RemainingRefundableCents,
  bool IsFullyRefunded,
  bool IsPartiallyRefunded
);