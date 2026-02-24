namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardOpenBoxDto(
  Guid FulfillmentGroupId,
  string Status,
  DateTimeOffset UpdatedAt,
  List<DashboardOrderSummaryDto> Orders);