namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardShippingInvoiceDto(
  Guid ShippingInvoiceId,
  Guid FulfillmentGroupId,
  long AmountCents,
  string CurrencyCode,
  string Status,
  string? Provider,
  string? ProviderCheckoutId,
  DateTimeOffset? PaidAt,
  DateTimeOffset CreatedAt);