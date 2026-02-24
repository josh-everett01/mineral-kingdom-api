namespace MineralKingdom.Contracts.Dashboard;

public sealed record DashboardDto(
  List<DashboardWonAuctionDto> WonAuctions,
  List<DashboardOrderSummaryDto> UnpaidAuctionOrders,
  List<DashboardOrderSummaryDto> PaidOrders,
  DashboardOpenBoxDto? OpenBox,
  List<DashboardShippingInvoiceDto> ShippingInvoices);