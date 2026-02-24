namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardOrderSummaryDto(
  Guid OrderId,
  string OrderNumber,
  string SourceType,
  string Status,
  int TotalCents,
  string CurrencyCode,
  DateTimeOffset CreatedAt,
  DateTimeOffset? PaymentDueAt,
  Guid? FulfillmentGroupId);