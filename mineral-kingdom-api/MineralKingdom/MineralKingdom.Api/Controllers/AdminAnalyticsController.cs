using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Analytics;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Contracts.Listings;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = $"{UserRoles.Owner},{UserRoles.Staff}")]
public sealed class AdminAnalyticsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminAnalyticsController(MineralKingdomDbContext db) => _db = db;

  [HttpGet("overview")]
  public async Task<IActionResult> Overview([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    // inclusive range [fromDate, toDate]
    var start = fromDate.Date;
    var end = toDate.Date;

    var salesRows = await _db.DailySalesSummaries.AsNoTracking()
      .Where(x => x.Date >= start && x.Date <= end)
      .ToListAsync(ct);

    var gross = salesRows.Sum(x => x.GrossSalesCents);
    var count = salesRows.Sum(x => x.OrderCount);
    var store = salesRows.Sum(x => x.StoreSalesCents);
    var auction = salesRows.Sum(x => x.AuctionSalesCents);
    var aov = count == 0 ? 0 : gross / count;

    var auctionRows = await _db.DailyAuctionSummaries.AsNoTracking()
      .Where(x => x.Date >= start && x.Date <= end)
      .ToListAsync(ct);

    var auctionsClosed = auctionRows.Sum(x => x.AuctionsClosed);
    var auctionsSold = auctionRows.Sum(x => x.AuctionsSold);
    var auctionsUnsold = auctionRows.Sum(x => x.AuctionsUnsold);

    // For v1, compute simple weighted-ish aggregates where possible
    int? avgFinalPrice = null;
    var finalPriceSamples = auctionRows.Where(x => x.AvgFinalPriceCents.HasValue).Select(x => x.AvgFinalPriceCents!.Value).ToList();
    if (finalPriceSamples.Count > 0) avgFinalPrice = (int)Math.Round(finalPriceSamples.Average());

    double? avgBids = null;
    var bidSamples = auctionRows.Where(x => x.AvgBidsPerAuction.HasValue).Select(x => x.AvgBidsPerAuction!.Value).ToList();
    if (bidSamples.Count > 0) avgBids = bidSamples.Average();

    double? reserveMetRate = null;
    var reserveSamples = auctionRows.Where(x => x.ReserveMetRate.HasValue).Select(x => x.ReserveMetRate!.Value).ToList();
    if (reserveSamples.Count > 0) reserveMetRate = reserveSamples.Average();

    double? paymentCompletion = null;
    var paySamples = auctionRows.Where(x => x.PaymentCompletionRate.HasValue).Select(x => x.PaymentCompletionRate!.Value).ToList();
    if (paySamples.Count > 0) paymentCompletion = paySamples.Average();

    var inventory = await InventoryStatusInternalAsync(ct);

    var dto = new AnalyticsOverviewDto(
      new SalesOverviewDto(start, end, gross, count, aov, store, auction),
      new AuctionOverviewDto(start, end, auctionsClosed, auctionsSold, auctionsUnsold, avgFinalPrice, avgBids, reserveMetRate, paymentCompletion),
      inventory
    );

    return Ok(dto);
  }

  [HttpGet("sales/timeseries")]
  public async Task<IActionResult> SalesTimeseries([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    var start = fromDate.Date;
    var end = toDate.Date;

    var rows = await _db.DailySalesSummaries.AsNoTracking()
      .Where(x => x.Date >= start && x.Date <= end)
      .OrderBy(x => x.Date)
      .Select(x => new SalesDayPointDto(
        x.Date,
        x.GrossSalesCents,
        x.OrderCount,
        x.AovCents,
        x.StoreSalesCents,
        x.AuctionSalesCents))
      .ToListAsync(ct);

    return Ok(rows);
  }

  [HttpGet("auctions/timeseries")]
  public async Task<IActionResult> AuctionsTimeseries([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    var start = fromDate.Date;
    var end = toDate.Date;

    var rows = await _db.DailyAuctionSummaries.AsNoTracking()
      .Where(x => x.Date >= start && x.Date <= end)
      .OrderBy(x => x.Date)
      .Select(x => new AuctionDayPointDto(
        x.Date,
        x.AuctionsClosed,
        x.AuctionsSold,
        x.AuctionsUnsold,
        x.AvgFinalPriceCents,
        x.AvgBidsPerAuction,
        x.ReserveMetRate,
        x.PaymentCompletionRate))
      .ToListAsync(ct);

    return Ok(rows);
  }

  [HttpGet("inventory/status")]
  public async Task<IActionResult> InventoryStatus(CancellationToken ct)
    => Ok(await InventoryStatusInternalAsync(ct));

  private async Task<InventoryStatusDto> InventoryStatusInternalAsync(CancellationToken ct)
  {
    const int lowStockThreshold = 3;

    var publishedListings = await _db.Listings.AsNoTracking()
      .CountAsync(l => l.Status == ListingStatuses.Published && l.QuantityAvailable > 0, ct);

    var lowStock = await _db.Listings.AsNoTracking()
      .CountAsync(l => l.Status == ListingStatuses.Published && l.QuantityAvailable > 0 && l.QuantityAvailable <= lowStockThreshold, ct);

    var now = DateTimeOffset.UtcNow;
    var endingSoon = now.AddHours(24);

    var activeAuctions = await _db.Auctions.AsNoTracking()
      .CountAsync(a => a.Status == "LIVE", ct);

    var auctionsEndingSoon = await _db.Auctions.AsNoTracking()
      .CountAsync(a => a.Status == "LIVE" && a.CloseTime <= endingSoon, ct);

    return new InventoryStatusDto(publishedListings, lowStock, activeAuctions, auctionsEndingSoon);
  }

  private static bool TryParseDate(string s, out DateTime dt)
  {
    // expects yyyy-MM-dd
    return DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt);
  }
}