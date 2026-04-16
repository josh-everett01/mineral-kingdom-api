namespace MineralKingdom.Contracts.Store;

public sealed record CreateOrderRequest(
  List<CreateOrderLineRequest> Lines
);

public sealed record CreateOrderLineRequest(
  Guid OfferId,
  int Quantity
);

public sealed record OrderIdResponse(Guid Id);

public sealed record OrderDto(
  Guid Id,
  Guid? UserId,
  string OrderNumber,
  string SourceType,
  Guid? AuctionId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? PaymentDueAt,
  string ShippingMode,
  bool RequiresShippingInvoice,
  int ShippingAmountCents,
  int SubtotalCents,
  int DiscountTotalCents,
  int TotalCents,
  string CurrencyCode,
  string Status,
  string? PaymentStatus,
  string? PaymentProvider,
  DateTimeOffset? PaidAt,
  Guid? FulfillmentGroupId,
  string? FulfillmentStatus,
  string? BoxStatus,
  string? ShipmentRequestStatus,
  DateTimeOffset? PackedAt,
  DateTimeOffset? ShippedAt,
  DateTimeOffset? DeliveredAt,
  string? ShippingCarrier,
  string? TrackingNumber,
  Guid? ShippingInvoiceId,
  string? ShippingInvoiceStatus,
  OrderStatusHistoryDto StatusHistory,
  List<OrderLineDto> Lines
);

public sealed record OrderStatusHistoryDto(
  List<OrderTimelineEntryDto> Entries
);

public sealed record OrderTimelineEntryDto(
  string Type,
  string Title,
  string? Description,
  DateTimeOffset OccurredAt
);

public sealed record OrderLineDto(
  Guid Id,
  Guid? OfferId,
  Guid ListingId,
  string? ListingSlug,
  string Title,
  string? PrimaryImageUrl,
  string? MineralName,
  string? Locality,
  int UnitPriceCents,
  int UnitDiscountCents,
  int UnitFinalPriceCents,
  int Quantity,
  int LineSubtotalCents,
  int LineDiscountCents,
  int LineTotalCents
);