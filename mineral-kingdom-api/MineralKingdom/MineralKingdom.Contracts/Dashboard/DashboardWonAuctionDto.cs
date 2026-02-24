namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardWonAuctionDto(
  Guid AuctionId,
  Guid ListingId,
  int CurrentPriceCents,
  DateTimeOffset CloseTime,
  string Status);