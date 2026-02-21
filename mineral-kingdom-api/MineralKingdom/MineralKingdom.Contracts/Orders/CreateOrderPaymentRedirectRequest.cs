namespace MineralKingdom.Contracts.Orders;

public sealed record CreateOrderPaymentRedirectRequest(
  Guid OrderId,
  Guid OrderPaymentId,
  int AmountCents,
  string CurrencyCode,
  string SuccessUrl,
  string CancelUrl,
  IReadOnlyList<OrderPaymentLineItem> LineItems
);

public sealed record OrderPaymentLineItem(
  string Name,
  int Quantity,
  int UnitAmountCents
);
