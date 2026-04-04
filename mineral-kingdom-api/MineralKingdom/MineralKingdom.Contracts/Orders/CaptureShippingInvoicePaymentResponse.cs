namespace MineralKingdom.Contracts.Orders;

public sealed record CaptureShippingInvoicePaymentResponse(
  Guid ShippingInvoiceId,
  string Provider,
  string PaymentStatus,
  string? ProviderPaymentId
);