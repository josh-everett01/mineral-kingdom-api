namespace MineralKingdom.Contracts.Orders;

public sealed record StartOrderPaymentResponse(
  Guid OrderPaymentId,
  string Provider,
  string Status,
  string RedirectUrl
);
