namespace MineralKingdom.Contracts.Store;

public sealed record CheckoutHeartbeatRequest(Guid HoldId);

public sealed record CheckoutHeartbeatResponse(
  Guid HoldId,
  DateTimeOffset ExpiresAt,
  bool CanExtend,
  int ExtensionCount,
  int MaxExtensions
);