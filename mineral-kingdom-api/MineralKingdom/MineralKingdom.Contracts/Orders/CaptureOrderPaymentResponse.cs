namespace MineralKingdom.Contracts.Orders;

public sealed record CaptureOrderPaymentResponse(
  Guid PaymentId,
  string Provider,
  string PaymentStatus,
  string? ProviderPaymentId
);