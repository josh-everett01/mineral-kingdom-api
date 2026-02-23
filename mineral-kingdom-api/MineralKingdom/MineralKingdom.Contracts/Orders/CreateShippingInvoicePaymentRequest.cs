namespace MineralKingdom.Contracts.Orders;

public sealed record CreateShippingInvoicePaymentRequest(
  string Provider,         // STRIPE | PAYPAL
  string SuccessUrl,
  string CancelUrl);

public sealed record CreateShippingInvoicePaymentRedirectResult(
  string ProviderCheckoutId,
  string RedirectUrl);