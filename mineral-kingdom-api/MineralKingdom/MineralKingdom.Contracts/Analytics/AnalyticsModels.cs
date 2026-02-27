namespace MineralKingdom.Contracts.Analytics;

public sealed record SalesOverviewDto(
  DateTime FromDateUtc,
  DateTime ToDateUtc,
  long GrossSalesCents,
  int OrderCount,
  long AovCents,
  long StoreSalesCents,
  long AuctionSalesCents
);

public sealed record AuctionOverviewDto(
  DateTime FromDateUtc,
  DateTime ToDateUtc,
  int AuctionsClosed,
  int AuctionsSold,
  int AuctionsUnsold,
  int? AvgFinalPriceCents,
  double? AvgBidsPerAuction,
  double? ReserveMetRate,
  double? PaymentCompletionRate
);

public sealed record AnalyticsOverviewDto(
  SalesOverviewDto Sales,
  AuctionOverviewDto Auctions,
  InventoryStatusDto Inventory
);

public sealed record SalesDayPointDto(
  DateTime DateUtc,
  long GrossSalesCents,
  int OrderCount,
  long AovCents,
  long StoreSalesCents,
  long AuctionSalesCents
);

public sealed record AuctionDayPointDto(
  DateTime DateUtc,
  int AuctionsClosed,
  int AuctionsSold,
  int AuctionsUnsold,
  int? AvgFinalPriceCents,
  double? AvgBidsPerAuction,
  double? ReserveMetRate,
  double? PaymentCompletionRate
);

public sealed record InventoryStatusDto(
  int PublishedListings,
  int LowStockListings,
  int ActiveAuctions,
  int AuctionsEndingSoon
);