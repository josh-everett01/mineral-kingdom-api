using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities.Analytics;

namespace MineralKingdom.Infrastructure.Analytics;

public sealed class AnalyticsSnapshotService
{
  private readonly MineralKingdomDbContext _db;

  public AnalyticsSnapshotService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public async Task GenerateDailyAsync(DateTime utcDate, DateTimeOffset now, CancellationToken ct)
  {
    // Normalize to date-only (UTC)
    var date = utcDate.Date;
    var start = new DateTimeOffset(date, TimeSpan.Zero);
    var end = start.AddDays(1);

    // ===== Sales snapshot =====
    var paidOrders = await _db.Orders
      .AsNoTracking()
      .Where(o => o.PaidAt != null
                  && o.PaidAt >= start
                  && o.PaidAt < end
                  && o.Status == "PAID")
      .Select(o => new { o.TotalCents, o.SourceType })
      .ToListAsync(ct);

    var orderCount = paidOrders.Count;
    long gross = paidOrders.Sum(x => (long)x.TotalCents);
    long store = paidOrders.Where(x => x.SourceType == "STORE").Sum(x => (long)x.TotalCents);
    long auction = paidOrders.Where(x => x.SourceType == "AUCTION").Sum(x => (long)x.TotalCents);
    long aov = orderCount == 0 ? 0 : gross / orderCount;

    var salesRow = await _db.DailySalesSummaries.SingleOrDefaultAsync(x => x.Date == date, ct);
    if (salesRow is null)
    {
      salesRow = new DailySalesSummary { Date = date };
      _db.DailySalesSummaries.Add(salesRow);
    }

    salesRow.GrossSalesCents = gross;
    salesRow.NetSalesCents = gross; // v1 minimal
    salesRow.OrderCount = orderCount;
    salesRow.AovCents = aov;
    salesRow.StoreSalesCents = store;
    salesRow.AuctionSalesCents = auction;
    salesRow.CreatedAt = now;

    // ===== Auction snapshot =====
    var closed = await _db.Auctions
      .AsNoTracking()
      .Where(a => a.CloseTime >= start && a.CloseTime < end)
      .Select(a => new { a.Status, a.CurrentPriceCents, a.BidCount, a.ReserveMet })
      .ToListAsync(ct);

    int auctionsClosed = closed.Count;

    bool IsSold(string status)
      => status == "CLOSED_PAID" || status == "CLOSED_WAITING_ON_PAYMENT";

    bool IsPaid(string status) => status == "CLOSED_PAID";
    bool IsUnsold(string status) => status == "CLOSED_NOT_SOLD";

    int soldCount = closed.Count(a => IsSold(a.Status));
    int paidCount = closed.Count(a => IsPaid(a.Status));
    int unsoldCount = closed.Count(a => IsUnsold(a.Status));

    int? avgFinalPrice = soldCount == 0 ? null : (int)Math.Round(closed.Where(a => IsSold(a.Status)).Average(a => a.CurrentPriceCents));
    double? avgBids = auctionsClosed == 0 ? null : closed.Average(a => (double)a.BidCount);

    double? reserveMetRate = auctionsClosed == 0 ? null : closed.Count(a => a.ReserveMet) / (double)auctionsClosed;
    double? paymentCompletionRate = soldCount == 0 ? null : paidCount / (double)soldCount;

    var auctionRow = await _db.DailyAuctionSummaries.SingleOrDefaultAsync(x => x.Date == date, ct);
    if (auctionRow is null)
    {
      auctionRow = new DailyAuctionSummary { Date = date };
      _db.DailyAuctionSummaries.Add(auctionRow);
    }

    auctionRow.AuctionsClosed = auctionsClosed;
    auctionRow.AuctionsSold = soldCount;
    auctionRow.AuctionsUnsold = unsoldCount;
    auctionRow.AvgFinalPriceCents = avgFinalPrice;
    auctionRow.AvgBidsPerAuction = avgBids;
    auctionRow.ReserveMetRate = reserveMetRate;
    auctionRow.PaymentCompletionRate = paymentCompletionRate;
    auctionRow.CreatedAt = now;

    await _db.SaveChangesAsync(ct);
  }
}