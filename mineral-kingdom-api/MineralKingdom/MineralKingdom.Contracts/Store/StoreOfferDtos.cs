using System;

namespace MineralKingdom.Contracts.Store;

public sealed record StoreOfferDto(
  Guid Id,
  Guid ListingId,
  int PriceCents,
  string DiscountType,
  int? DiscountCents,
  int? DiscountPercentBps,
  bool IsActive,
  DateTimeOffset? StartsAt,
  DateTimeOffset? EndsAt,
  int EffectivePriceCents
);

public sealed record UpsertStoreOfferRequest(
  Guid ListingId,
  int PriceCents,
  string DiscountType,
  int? DiscountCents,
  int? DiscountPercentBps,
  bool IsActive,
  DateTimeOffset? StartsAt,
  DateTimeOffset? EndsAt
);

public sealed record StoreOfferIdResponse(Guid Id);
