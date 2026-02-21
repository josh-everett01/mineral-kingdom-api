namespace MineralKingdom.Contracts.Orders;

public sealed record StartOrderPaymentRequest(
  string Provider,     // PaymentProviders.Stripe | PaymentProviders.PayPal
  string SuccessUrl,
  string CancelUrl
);
