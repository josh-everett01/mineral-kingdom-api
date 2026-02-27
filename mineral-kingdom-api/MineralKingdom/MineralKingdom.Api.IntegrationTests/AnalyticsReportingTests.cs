using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Analytics;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AnalyticsReportingTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AnalyticsReportingTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Snapshot_is_idempotent_for_same_date()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var date = new DateTime(2026, 02, 26, 0, 0, 0, DateTimeKind.Utc);
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Seed one paid store order on that day
      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = null,
        GuestEmail = "guest@example.com",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CurrencyCode = "USD",
        Status = "PAID",
        CreatedAt = now,
        UpdatedAt = now,
        OrderNumber = "TST-ORDER-1",
        SourceType = "STORE",
        PaidAt = new DateTimeOffset(date.AddHours(12), TimeSpan.Zero),
        FulfillmentGroupId = null
      };
      db.Orders.Add(order);

      // Seed one closed paid auction on that day
      var listingId = Guid.NewGuid();
      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Test listing",
        Description = "x",
        Status = "PUBLISHED",
        QuantityAvailable = 5,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Status = "CLOSED_PAID",
        CurrentPriceCents = 2500,
        BidCount = 3,
        ReservePriceCents = 2000,
        ReserveMet = true,
        StartTime = new DateTimeOffset(date.AddHours(8), TimeSpan.Zero),
        CloseTime = new DateTimeOffset(date.AddHours(13), TimeSpan.Zero),
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<AnalyticsSnapshotService>();

      // Run twice
      await svc.GenerateDailyAsync(date, now, CancellationToken.None);
      await svc.GenerateDailyAsync(date, now, CancellationToken.None);
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var sales = await db.DailySalesSummaries.SingleAsync(x => x.Date == date.Date);
      sales.OrderCount.Should().Be(1);
      sales.GrossSalesCents.Should().Be(1000);
      sales.StoreSalesCents.Should().Be(1000);

      var auctions = await db.DailyAuctionSummaries.SingleAsync(x => x.Date == date.Date);
      auctions.AuctionsClosed.Should().Be(1);
      auctions.AuctionsSold.Should().Be(1);
      auctions.PaymentCompletionRate.Should().BeApproximately(1.0, 0.0001);

      // Ensure still only one row per date (idempotent)
      (await db.DailySalesSummaries.CountAsync(x => x.Date == date.Date)).Should().Be(1);
      (await db.DailyAuctionSummaries.CountAsync(x => x.Date == date.Date)).Should().Be(1);
    }
  }

  [Fact]
  public async Task Admin_analytics_overview_reads_from_snapshots()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var day = new DateTime(2026, 02, 27, 0, 0, 0, DateTimeKind.Utc);

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.DailySalesSummaries.Add(new Infrastructure.Persistence.Entities.Analytics.DailySalesSummary
      {
        Date = day.Date,
        GrossSalesCents = 5000,
        NetSalesCents = 5000,
        OrderCount = 2,
        AovCents = 2500,
        StoreSalesCents = 3000,
        AuctionSalesCents = 2000,
        CreatedAt = now
      });

      db.DailyAuctionSummaries.Add(new Infrastructure.Persistence.Entities.Analytics.DailyAuctionSummary
      {
        Date = day.Date,
        AuctionsClosed = 2,
        AuctionsSold = 1,
        AuctionsUnsold = 1,
        AvgFinalPriceCents = 2200,
        AvgBidsPerAuction = 3.5,
        ReserveMetRate = 0.5,
        PaymentCompletionRate = 1.0,
        CreatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var admin = factory.CreateClient();
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Staff);

    var res = await admin.GetAsync("/api/admin/analytics/overview?from=2026-02-27&to=2026-02-27");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await res.Content.ReadAsStringAsync();
    json.Should().Contain("\"grossSalesCents\":5000");
    json.Should().Contain("\"orderCount\":2");
  }

  [Fact]
  public async Task Admin_exports_return_csv_with_headers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var date = new DateTime(2026, 02, 26, 0, 0, 0, DateTimeKind.Utc);
    var now = DateTimeOffset.UtcNow;

    Guid listingId;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.Add(new Order
      {
        Id = Guid.NewGuid(),
        UserId = null,
        GuestEmail = "csv@example.com",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CurrencyCode = "USD",
        Status = "PAID",
        CreatedAt = now,
        UpdatedAt = now,
        OrderNumber = "CSV-ORDER-1",
        SourceType = "STORE",
        PaidAt = new DateTimeOffset(date.AddHours(10), TimeSpan.Zero),
        FulfillmentGroupId = null
      });

      listingId = Guid.NewGuid();
      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "CSV listing",
        Description = "x",
        Status = "PUBLISHED",
        QuantityAvailable = 5,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Status = "CLOSED_PAID",
        CurrentPriceCents = 2500,
        BidCount = 3,
        ReservePriceCents = 2000,
        ReserveMet = true,
        StartTime = new DateTimeOffset(date.AddHours(8), TimeSpan.Zero),
        CloseTime = new DateTimeOffset(date.AddHours(13), TimeSpan.Zero),
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var admin = factory.CreateClient();
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var orders = await admin.GetAsync("/api/admin/exports/orders.csv?from=2026-02-26&to=2026-02-26");
    orders.StatusCode.Should().Be(HttpStatusCode.OK);
    orders.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    var ordersCsv = await orders.Content.ReadAsStringAsync();
    ordersCsv.Split('\n')[0].Trim().Should().Be("order_number,date_paid,customer_email,source,subtotal_cents,discount_total_cents,total_cents,status");

    var auctions = await admin.GetAsync("/api/admin/exports/auctions.csv?from=2026-02-26&to=2026-02-26");
    auctions.StatusCode.Should().Be(HttpStatusCode.OK);
    auctions.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    var auctionsCsv = await auctions.Content.ReadAsStringAsync();
    auctionsCsv.Split('\n')[0].Trim().Should().Be("auction_id,listing_title,close_time,final_price_cents,bid_count,reserve_met,status");
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}