namespace MineralKingdom.Contracts.Orders;

public sealed record OrderPaymentConfirmationResponse(
  Guid PaymentId,
  string Provider,
  string PaymentStatus,
  bool IsConfirmed,
  Guid? OrderId,
  string? OrderNumber,
  string? OrderStatus,
  int? OrderTotalCents,
  string? OrderCurrencyCode
);