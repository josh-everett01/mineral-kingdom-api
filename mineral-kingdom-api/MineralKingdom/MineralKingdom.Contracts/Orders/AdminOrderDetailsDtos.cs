namespace MineralKingdom.Contracts.Orders;

public sealed record AdminOrderDetailDto(
  Guid Id,
  string OrderNumber,
  string Status,
  string SourceType,
  Guid? UserId,
  string? GuestEmail,
  string? CustomerEmail,
  string CurrencyCode,
  int SubtotalCents,
  int DiscountTotalCents,
  int ShippingAmountCents,
  int TotalCents,
  DateTimeOffset? PaymentDueAt,
  DateTimeOffset? PaidAt,
  Guid? AuctionId,
  string? ShippingMode,
  long TotalRefundedCents,
  long RemainingRefundableCents,
  bool IsFullyRefunded,
  bool IsPartiallyRefunded,
  bool CanRefund,
  List<string> AvailableRefundProviders,
  List<AdminOrderPaymentSummaryDto> Payments,
  List<AdminOrderRefundHistoryItemDto> RefundHistory,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt
);

public sealed record AdminOrderPaymentSummaryDto(
  string Provider,
  string Status,
  long AmountCents,
  string CurrencyCode,
  string? ProviderPaymentId,
  string? ProviderCheckoutId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt
);

public sealed record AdminOrderRefundHistoryItemDto(
  Guid RefundId,
  long AmountCents,
  string CurrencyCode,
  string Provider,
  string? ProviderRefundId,
  string? Reason,
  DateTimeOffset CreatedAt
);