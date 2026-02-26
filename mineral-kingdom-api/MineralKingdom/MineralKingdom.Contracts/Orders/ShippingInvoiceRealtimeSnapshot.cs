namespace MineralKingdom.Contracts.Orders;

public sealed record ShippingInvoiceRealtimeSnapshot(
  Guid ShippingInvoiceId,
  Guid FulfillmentGroupId,
  string Status,
  DateTimeOffset? PaidAt,
  long AmountCents,
  string CurrencyCode,
  string? Provider,
  string? ProviderCheckoutId,
  DateTimeOffset UpdatedAt
);