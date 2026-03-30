namespace MineralKingdom.Contracts.Orders;

public sealed record ShippingInvoiceDetailRelatedOrderDto(
  Guid OrderId,
  string OrderNumber,
  string SourceType
);

public sealed record ShippingInvoiceDetailItemDto(
  Guid? OrderId,
  string? OrderNumber,
  string? SourceType,
  Guid? ListingId,
  string? ListingSlug,
  string? Title,
  string? PrimaryImageUrl,
  string? MineralName,
  string? Locality,
  int Quantity
);

public sealed record ShippingInvoiceDetailDto(
  Guid ShippingInvoiceId,
  Guid FulfillmentGroupId,
  long AmountCents,
  string CurrencyCode,
  string Status,
  string? Provider,
  string? ProviderCheckoutId,
  DateTimeOffset? PaidAt,
  DateTimeOffset? DueAt,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  int ItemCount,
  string? PreviewTitle,
  string? PreviewImageUrl,
  int AuctionOrderCount,
  int StoreOrderCount,
  List<ShippingInvoiceDetailRelatedOrderDto> RelatedOrders,
  List<ShippingInvoiceDetailItemDto> Items
);