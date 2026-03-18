namespace MineralKingdom.Contracts.Store;

public sealed record CheckoutPaymentRealtimeSnapshot(
  Guid PaymentId,
  string Status,
  Guid? OrderId,
  Guid? HoldId,
  DateTimeOffset EmittedAt
);