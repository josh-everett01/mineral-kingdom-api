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
  int SubtotalCents,
  int DiscountTotalCents,
  int TotalCents,
  string CurrencyCode,
  string Status,
  List<OrderLineDto> Lines
);

public sealed record OrderLineDto(
  Guid Id,
  Guid OfferId,
  Guid ListingId,
  int UnitPriceCents,
  int UnitDiscountCents,
  int UnitFinalPriceCents,
  int Quantity,
  int LineSubtotalCents,
  int LineDiscountCents,
  int LineTotalCents
);
