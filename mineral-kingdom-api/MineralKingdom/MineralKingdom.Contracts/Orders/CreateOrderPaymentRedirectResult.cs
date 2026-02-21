namespace MineralKingdom.Contracts.Orders;

public sealed record CreateOrderPaymentRedirectResult(
  string ProviderCheckoutId,
  string RedirectUrl
);
