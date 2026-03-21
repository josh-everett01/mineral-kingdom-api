using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CheckoutWebhookRecoveryTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CheckoutWebhookRecoveryTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task ConfirmPaidFromWebhookAsync_when_hold_is_completed_but_order_missing_recovers_and_creates_order()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var seeded = await SeedCompletedHoldWithoutOrderAsync(factory, priceCents: 24900);

    using var scope = factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<CheckoutService>();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var result = await svc.ConfirmPaidFromWebhookAsync(
      seeded.HoldId,
      paymentReference: "paypal-capture-recovery-1",
      now: DateTimeOffset.UtcNow,
      ct: CancellationToken.None);

    result.Ok.Should().BeTrue();
    result.Error.Should().BeNull();

    var hold = await db.CheckoutHolds.SingleAsync(x => x.Id == seeded.HoldId);
    hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
    hold.PaymentReference.Should().Be("existing-payment-ref");

    var orders = await db.Orders
      .Include(x => x.Lines)
      .Where(x => x.CheckoutHoldId == seeded.HoldId)
      .ToListAsync();

    orders.Should().HaveCount(1);
    orders[0].Status.Should().Be("READY_TO_FULFILL");
    orders[0].GuestEmail.Should().Be("guest@example.com");
    orders[0].TotalCents.Should().Be(24900);
    orders[0].Lines.Should().HaveCount(1);

    var ledger = await db.OrderLedgerEntries
      .Where(x => x.OrderId == orders[0].Id)
      .OrderBy(x => x.CreatedAt)
      .ToListAsync();

    ledger.Select(x => x.EventType).Should().Contain(new[] { "PAYMENT_SUCCEEDED", "ORDER_CREATED" });
  }

  [Fact]
  public async Task ConfirmPaidFromWebhookAsync_is_idempotent_when_order_already_exists_for_hold()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var seeded = await SeedCompletedHoldWithoutOrderAsync(factory, priceCents: 21900);

    Guid existingOrderId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hold = await db.CheckoutHolds.SingleAsync(x => x.Id == seeded.HoldId);

      var checkoutService = scope.ServiceProvider.GetRequiredService<CheckoutService>();
      var order = await InvokeBuildPaidOrderFromHoldAsync(checkoutService, hold, DateTimeOffset.UtcNow, CancellationToken.None);

      db.Orders.Add(order);
      await db.SaveChangesAsync();

      existingOrderId = order.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<CheckoutService>();
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var result = await svc.ConfirmPaidFromWebhookAsync(
        seeded.HoldId,
        paymentReference: "paypal-capture-idempotent-1",
        now: DateTimeOffset.UtcNow,
        ct: CancellationToken.None);

      result.Ok.Should().BeTrue();
      result.Error.Should().BeNull();

      var orders = await db.Orders
        .Where(x => x.CheckoutHoldId == seeded.HoldId)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync();

      orders.Should().HaveCount(1);
      orders[0].Id.Should().Be(existingOrderId);
    }
  }

  [Fact]
  public async Task ConfirmPaidFromWebhookAsync_from_active_hold_marks_listing_sold_deactivates_offer_and_reconciles_other_carts()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var seeded = await SeedActiveHoldWithCompetingCartAsync(factory, priceCents: 18500);

    using var scope = factory.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<CheckoutService>();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var result = await svc.ConfirmPaidFromWebhookAsync(
      seeded.HoldId,
      paymentReference: "stripe-pi-test-1",
      now: DateTimeOffset.UtcNow,
      ct: CancellationToken.None);

    result.Ok.Should().BeTrue();
    result.Error.Should().BeNull();

    var hold = await db.CheckoutHolds.SingleAsync(x => x.Id == seeded.HoldId);
    hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
    hold.PaymentReference.Should().Be("stripe-pi-test-1");

    var purchaserCart = await db.Carts.SingleAsync(x => x.Id == seeded.PurchaserCartId);
    purchaserCart.Status.Should().Be(CartStatuses.CheckedOut);

    var listing = await db.Listings.SingleAsync(x => x.Id == seeded.ListingId);
    listing.Status.Should().Be(ListingStatuses.Sold);
    listing.QuantityAvailable.Should().Be(0);

    var offer = await db.StoreOffers.SingleAsync(x => x.Id == seeded.OfferId);
    offer.IsActive.Should().BeFalse();

    var holdItems = await db.CheckoutHoldItems
      .Where(x => x.HoldId == seeded.HoldId)
      .ToListAsync();

    holdItems.Should().NotBeEmpty();
    holdItems.All(x => x.IsActive == false).Should().BeTrue();

    var competingLines = await db.CartLines
      .Where(x => x.CartId == seeded.CompetingCartId && x.OfferId == seeded.OfferId)
      .ToListAsync();

    competingLines.Should().BeEmpty();

    var competingNotices = await db.CartNotices
      .Where(x => x.CartId == seeded.CompetingCartId && x.OfferId == seeded.OfferId)
      .ToListAsync();

    competingNotices.Should().NotBeEmpty();
    competingNotices.Any(x => x.Type == CartNoticeTypes.ItemRemovedSold).Should().BeTrue();

    var orders = await db.Orders
      .Include(x => x.Lines)
      .Where(x => x.CheckoutHoldId == seeded.HoldId)
      .ToListAsync();

    orders.Should().HaveCount(1);
    orders[0].Status.Should().Be("READY_TO_FULFILL");
    orders[0].TotalCents.Should().Be(18500);
    orders[0].Lines.Should().HaveCount(1);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<(Guid HoldId, Guid CartId, Guid ListingId, Guid OfferId)> SeedCompletedHoldWithoutOrderAsync(
    TestAppFactory factory,
    int priceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Recovered Order Listing",
      Description = "Recovered order listing",
      Status = ListingStatuses.Sold,
      IsFluorescent = false,
      IsLot = false,
      QuantityTotal = 1,
      QuantityAvailable = 0,
      CreatedAt = now,
      UpdatedAt = now
    };

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      PriceCents = priceCents,
      DiscountType = DiscountTypes.None,
      IsActive = false,
      EndsAt = now,
      CreatedAt = now,
      UpdatedAt = now
    };

    var cart = new Cart
    {
      Id = Guid.NewGuid(),
      Status = CartStatuses.CheckedOut,
      CreatedAt = now,
      UpdatedAt = now
    };

    var cartLine = new CartLine
    {
      Id = Guid.NewGuid(),
      CartId = cart.Id,
      OfferId = offer.Id,
      Quantity = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    var hold = new CheckoutHold
    {
      Id = Guid.NewGuid(),
      CartId = cart.Id,
      GuestEmail = "guest@example.com",
      Status = CheckoutHoldStatuses.Completed,
      ExpiresAt = now.AddMinutes(10),
      CreatedAt = now.AddMinutes(-5),
      UpdatedAt = now,
      CompletedAt = now.AddMinutes(-1),
      PaymentReference = "existing-payment-ref",
      ClientReturnedAt = now.AddMinutes(-2),
      ClientReturnReference = "provider-return-ref",
      ExtensionCount = 0
    };

    var holdItem = new CheckoutHoldItem
    {
      Id = Guid.NewGuid(),
      HoldId = hold.Id,
      ListingId = listing.Id,
      OfferId = offer.Id,
      IsActive = false,
      CreatedAt = now
    };

    db.Listings.Add(listing);
    db.StoreOffers.Add(offer);
    db.Carts.Add(cart);
    db.CartLines.Add(cartLine);
    db.CheckoutHolds.Add(hold);
    db.CheckoutHoldItems.Add(holdItem);

    await db.SaveChangesAsync();

    return (hold.Id, cart.Id, listing.Id, offer.Id);
  }

  private static async Task<(Guid HoldId, Guid PurchaserCartId, Guid CompetingCartId, Guid ListingId, Guid OfferId)> SeedActiveHoldWithCompetingCartAsync(
    TestAppFactory factory,
    int priceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Single Quantity Listing",
      Description = "Single quantity listing",
      Status = ListingStatuses.Published,
      IsFluorescent = false,
      IsLot = false,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      PriceCents = priceCents,
      DiscountType = DiscountTypes.None,
      IsActive = true,
      CreatedAt = now,
      UpdatedAt = now
    };

    var purchaserCart = new Cart
    {
      Id = Guid.NewGuid(),
      Status = CartStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now
    };

    var competingCart = new Cart
    {
      Id = Guid.NewGuid(),
      Status = CartStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now
    };

    var purchaserLine = new CartLine
    {
      Id = Guid.NewGuid(),
      CartId = purchaserCart.Id,
      OfferId = offer.Id,
      Quantity = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    var competingLine = new CartLine
    {
      Id = Guid.NewGuid(),
      CartId = competingCart.Id,
      OfferId = offer.Id,
      Quantity = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    var hold = new CheckoutHold
    {
      Id = Guid.NewGuid(),
      CartId = purchaserCart.Id,
      GuestEmail = "guest@example.com",
      Status = CheckoutHoldStatuses.Active,
      ExpiresAt = now.AddMinutes(10),
      CreatedAt = now,
      UpdatedAt = now,
      ExtensionCount = 0
    };

    var holdItem = new CheckoutHoldItem
    {
      Id = Guid.NewGuid(),
      HoldId = hold.Id,
      ListingId = listing.Id,
      OfferId = offer.Id,
      IsActive = true,
      CreatedAt = now
    };

    db.Listings.Add(listing);
    db.StoreOffers.Add(offer);
    db.Carts.AddRange(purchaserCart, competingCart);
    db.CartLines.AddRange(purchaserLine, competingLine);
    db.CheckoutHolds.Add(hold);
    db.CheckoutHoldItems.Add(holdItem);

    await db.SaveChangesAsync();

    return (hold.Id, purchaserCart.Id, competingCart.Id, listing.Id, offer.Id);
  }

  private static async Task<Order> InvokeBuildPaidOrderFromHoldAsync(
    CheckoutService checkoutService,
    CheckoutHold hold,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var method = typeof(CheckoutService).GetMethod(
      "BuildPaidOrderFromHoldAsync",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    method.Should().NotBeNull("BuildPaidOrderFromHoldAsync should exist");

    var task = (Task<Order>)method!.Invoke(checkoutService, new object[] { hold, now, ct })!;
    return await task;
  }
}