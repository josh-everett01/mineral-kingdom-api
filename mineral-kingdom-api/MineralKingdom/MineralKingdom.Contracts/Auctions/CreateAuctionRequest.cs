namespace MineralKingdom.Contracts.Auctions;

public sealed record CreateAuctionRequest(
  Guid ListingId,
  int StartingPriceCents,
  int? ReservePriceCents,
  int? QuotedShippingCents,
  string LaunchMode,
  string TimingMode,
  int? DurationHours,
  DateTimeOffset? StartTime,
  DateTimeOffset? CloseTime
);