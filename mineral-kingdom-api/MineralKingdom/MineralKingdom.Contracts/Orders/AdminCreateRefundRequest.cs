namespace MineralKingdom.Contracts.Orders;

public sealed record AdminCreateRefundRequest(
  long AmountCents,
  string Reason,
  string Provider);