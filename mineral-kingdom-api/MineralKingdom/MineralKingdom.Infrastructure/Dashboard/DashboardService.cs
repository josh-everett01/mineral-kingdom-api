using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Dashboard;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Dashboard;

public sealed class DashboardService
{
  private readonly MineralKingdomDbContext _db;

  // v1 limits (keep small and predictable)
  private const int Limit = 20;

  public DashboardService(MineralKingdomDbContext db) => _db = db;

  public async Task<DashboardDto> GetMyDashboardAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
  {
    // 1) Won auctions (finalized wins)
    var wonAuctions = await _db.Auctions.AsNoTracking()
      .Where(a =>
        a.CurrentLeaderUserId == userId &&
        a.Status == AuctionStatuses.ClosedPaid)
      .OrderByDescending(a => a.CloseTime)
      .Take(Limit)
      .Select(a => new DashboardWonAuctionDto(
        a.Id,
        a.ListingId,
        a.CurrentPriceCents,
        a.CloseTime,
        a.Status))
      .ToListAsync(ct);

    // 2) Unpaid auction orders + due dates
    var unpaidAuctionOrders = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.SourceType == "AUCTION" &&
        o.Status == "AWAITING_PAYMENT")
      .OrderBy(o => o.PaymentDueAt) // soonest due first
      .ThenByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new DashboardOrderSummaryDto(
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId))
      .ToListAsync(ct);

    // 3) Paid orders (ready to fulfill)
    var paidOrders = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.Status == "READY_TO_FULFILL")
      .OrderByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new DashboardOrderSummaryDto(
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId))
      .ToListAsync(ct);

    // 4) Open box (latest open box) + contained orders
    var openBox = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.UserId == userId && g.BoxStatus == "OPEN")
      .OrderByDescending(g => g.UpdatedAt)
      .Select(g => new
      {
        g.Id,
        g.Status,
        g.UpdatedAt
      })
      .FirstOrDefaultAsync(ct);

    DashboardOpenBoxDto? openBoxDto = null;

    if (openBox is not null)
    {
      var openBoxOrders = await _db.Orders.AsNoTracking()
        .Where(o => o.UserId == userId && o.FulfillmentGroupId == openBox.Id)
        .OrderByDescending(o => o.CreatedAt)
        .Take(Limit)
        .Select(o => new DashboardOrderSummaryDto(
          o.Id,
          o.OrderNumber,
          o.SourceType,
          o.Status,
          o.TotalCents,
          o.CurrencyCode,
          o.CreatedAt,
          o.PaymentDueAt,
          o.FulfillmentGroupId))
        .ToListAsync(ct);

      openBoxDto = new DashboardOpenBoxDto(
        openBox.Id,
        openBox.Status,
        openBox.UpdatedAt,
        openBoxOrders);
    }

    // 5) Shipping invoices for user groups (newest first)
    // Join invoices -> group to ensure group belongs to this user
    var invoices = await (
      from inv in _db.ShippingInvoices.AsNoTracking()
      join grp in _db.FulfillmentGroups.AsNoTracking()
        on inv.FulfillmentGroupId equals grp.Id
      where grp.UserId == userId
      orderby inv.CreatedAt descending
      select new DashboardShippingInvoiceDto(
        inv.Id,
        inv.FulfillmentGroupId,
        inv.AmountCents,
        inv.CurrencyCode,
        inv.Status,
        inv.Provider,
        inv.ProviderCheckoutId,
        inv.PaidAt,
        inv.CreatedAt
      )
    )
    .Take(Limit)
    .ToListAsync(ct);

    return new DashboardDto(
      WonAuctions: wonAuctions,
      UnpaidAuctionOrders: unpaidAuctionOrders,
      PaidOrders: paidOrders,
      OpenBox: openBoxDto,
      ShippingInvoices: invoices);
  }
}