using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrderTotalsSnapshotTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public OrderTotalsSnapshotTests(PostgresContainerFixture pg) => _pg = pg;

  // Any user id is fine here since we're calling OrderService directly (not an API endpoint).
  private static readonly Guid BuyerId = Guid.Parse("00000000-0000-0000-0000-000000000001");

  [Fact]
  public async Task Flat_discount_snapshots_totals_correctly()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listingId = await SeedListingAsync(factory);

    var offerId = await SeedOfferAsync(factory,
      listingId: listingId,
      priceCents: 1000,
      discountType: DiscountTypes.Flat,
      discountCents: 200,
      discountPercentBps: null);

    var (ok, err, order) = await CreateDraftOrderAsync(factory,
      userId: BuyerId,
      new List<OrderService.CreateLine> { new(offerId, 2) });

    ok.Should().BeTrue(err);
    order.Should().NotBeNull();

    order!.SubtotalCents.Should().Be(2000);
    order.DiscountTotalCents.Should().Be(400);
    order.TotalCents.Should().Be(1600);

    order.Lines.Should().HaveCount(1);
    var line = order.Lines.Single();

    line.UnitPriceCents.Should().Be(1000);
    line.UnitDiscountCents.Should().Be(200);
    line.UnitFinalPriceCents.Should().Be(800);

    line.Quantity.Should().Be(2);

    line.LineSubtotalCents.Should().Be(2000);
    line.LineDiscountCents.Should().Be(400);
    line.LineTotalCents.Should().Be(1600);
  }

  [Fact]
  public async Task Percent_discount_snapshots_totals_correctly_flooring_basis_points()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listingId = await SeedListingAsync(factory);

    // price 999, 25% => floor(999*2500/10000)=249, final=750
    var offerId = await SeedOfferAsync(factory,
      listingId: listingId,
      priceCents: 999,
      discountType: DiscountTypes.Percent,
      discountCents: null,
      discountPercentBps: 2500);

    var (ok, err, order) = await CreateDraftOrderAsync(factory,
      userId: BuyerId,
      new List<OrderService.CreateLine> { new(offerId, 3) });

    ok.Should().BeTrue(err);
    order.Should().NotBeNull();

    order!.SubtotalCents.Should().Be(999 * 3);       // 2997
    order.DiscountTotalCents.Should().Be(249 * 3);   // 747
    order.TotalCents.Should().Be(750 * 3);           // 2250

    var line = order.Lines.Single();
    line.UnitDiscountCents.Should().Be(249);
    line.UnitFinalPriceCents.Should().Be(750);
  }

  [Fact]
  public async Task Discount_is_clamped_to_price_so_total_never_goes_negative()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listingId = await SeedListingAsync(factory);

    // price 500, flat discount 999 => clamp to 500, final=0
    var offerId = await SeedOfferAsync(factory,
      listingId: listingId,
      priceCents: 500,
      discountType: DiscountTypes.Flat,
      discountCents: 999,
      discountPercentBps: null);

    var (ok, err, order) = await CreateDraftOrderAsync(factory,
      userId: BuyerId,
      new List<OrderService.CreateLine> { new(offerId, 1) });

    ok.Should().BeTrue(err);
    order.Should().NotBeNull();

    order!.SubtotalCents.Should().Be(500);
    order.DiscountTotalCents.Should().Be(500);
    order.TotalCents.Should().Be(0);

    var line = order.Lines.Single();
    line.UnitFinalPriceCents.Should().Be(0);
    line.LineTotalCents.Should().Be(0);
  }

  // -----------------------
  // helpers
  // -----------------------

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<Guid> SeedListingAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);
    await db.SaveChangesAsync();

    return listing.Id;
  }

  private static async Task<Guid> SeedOfferAsync(
    TestAppFactory factory,
    Guid listingId,
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      PriceCents = priceCents,
      DiscountType = discountType,
      DiscountCents = discountCents,
      DiscountPercentBps = discountPercentBps,
      IsActive = true,
      StartsAt = null,
      EndsAt = null,
      CreatedAt = now,
      UpdatedAt = now,
      DeletedAt = null
    };

    db.StoreOffers.Add(offer);
    await db.SaveChangesAsync();

    return offer.Id;
  }

  private static async Task<(bool Ok, string? Error, Order? Order)> CreateDraftOrderAsync(
    TestAppFactory factory,
    Guid userId,
    List<OrderService.CreateLine> lines)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var svc = new OrderService(db);
    return await svc.CreateDraftAsync(userId, lines, CancellationToken.None);
  }
}
