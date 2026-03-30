namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardShippingInvoiceRelatedOrderDto(
  Guid OrderId,
  string OrderNumber,
  string SourceType
);

public sealed record DashboardShippingInvoiceDto(
  Guid ShippingInvoiceId,
  Guid FulfillmentGroupId,
  long AmountCents,
  string CurrencyCode,
  string Status,
  string? Provider,
  string? ProviderCheckoutId,
  DateTimeOffset? PaidAt,
  DateTimeOffset CreatedAt,
  int ItemCount,
  string? PreviewTitle,
  string? PreviewImageUrl,
  int AuctionOrderCount,
  int StoreOrderCount,
  List<DashboardShippingInvoiceRelatedOrderDto> RelatedOrders);