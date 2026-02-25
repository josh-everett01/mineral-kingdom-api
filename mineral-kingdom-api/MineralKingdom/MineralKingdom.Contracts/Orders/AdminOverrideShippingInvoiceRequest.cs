namespace MineralKingdom.Contracts.Orders;

public sealed record AdminOverrideShippingInvoiceRequest(
  long AmountCents,
  string Reason);