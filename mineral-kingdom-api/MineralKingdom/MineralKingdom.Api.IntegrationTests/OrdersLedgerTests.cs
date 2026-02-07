using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrdersLedgerTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public OrdersLedgerTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Paid_order_is_created_on_payment_confirmation_and_has_ledger_entries()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1234);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var hold = await StartCheckoutAsync(client, cartId, email: "guest@example.com");

    using (var scope = factory.Services.CreateScope())
    {
      var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
      var (ok, err) = await checkout.ConfirmPaidFromWebhookAsync(hold.HoldId, "wh_pay_ledger_1", DateTimeOffset.UtcNow, CancellationToken.None);
      ok.Should().BeTrue(err);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var order = await db.Orders
        .Include(o => o.Lines)
        .SingleAsync(o => o.CheckoutHoldId == hold.HoldId);

      order.Status.Should().Be("PAID");
      order.PaidAt.Should().NotBeNull();
      order.OrderNumber.Should().NotBeNullOrWhiteSpace();
      order.GuestEmail.Should().Be("guest@example.com");
      order.Lines.Should().NotBeEmpty();
      order.TotalCents.Should().BeGreaterThan(0);

      var ledgerTypes = await db.OrderLedgerEntries
        .Where(e => e.OrderId == order.Id)
        .Select(e => e.EventType)
        .ToListAsync();

      ledgerTypes.Should().Contain("PAYMENT_SUCCEEDED");
      ledgerTypes.Should().Contain("ORDER_CREATED");
    }
  }

  [Fact]
  public async Task Guest_order_lookup_returns_paid_order_by_orderNumber_and_email()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var hold = await StartCheckoutAsync(client, cartId, email: "guest@example.com");

    using (var scope = factory.Services.CreateScope())
    {
      var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
      var (ok, err) = await checkout.ConfirmPaidFromWebhookAsync(hold.HoldId, "wh_pay_lookup_1", DateTimeOffset.UtcNow, CancellationToken.None);
      ok.Should().BeTrue(err);
    }

    string orderNumber;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      orderNumber = await db.Orders
        .Where(o => o.CheckoutHoldId == hold.HoldId)
        .Select(o => o.OrderNumber)
        .SingleAsync();
    }

    var res = await client.GetAsync($"/api/orders/lookup?orderNumber={Uri.EscapeDataString(orderNumber)}&email={Uri.EscapeDataString("guest@example.com")}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<OrderDto>();
    dto.Should().NotBeNull();
    dto!.Status.Should().Be("PAID");
    dto.OrderNumber.Should().Be(orderNumber);
  }

  [Fact]
  public async Task Webhook_retry_is_idempotent_and_does_not_create_duplicate_orders_or_duplicate_ORDER_CREATED_entry()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 2500);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var hold = await StartCheckoutAsync(client, cartId, email: "guest@example.com");

    using (var scope = factory.Services.CreateScope())
    {
      var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

      var (ok1, err1) = await checkout.ConfirmPaidFromWebhookAsync(hold.HoldId, "wh_pay_retry_1", DateTimeOffset.UtcNow, CancellationToken.None);
      ok1.Should().BeTrue(err1);

      var (ok2, err2) = await checkout.ConfirmPaidFromWebhookAsync(hold.HoldId, "wh_pay_retry_1", DateTimeOffset.UtcNow, CancellationToken.None);
      ok2.Should().BeTrue(err2);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var orders = await db.Orders
        .Where(o => o.CheckoutHoldId == hold.HoldId)
        .ToListAsync();

      orders.Should().HaveCount(1);

      var order = orders.Single();

      var createdCount = await db.OrderLedgerEntries
        .Where(e => e.OrderId == order.Id && e.EventType == "ORDER_CREATED")
        .CountAsync();

      createdCount.Should().Be(1);
    }
  }

  // ---------------- helpers (copied pattern from CheckoutHoldsTests) ----------------

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<string> SeedOfferAsync(TestAppFactory factory, int priceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Test Listing",
      Description = "Test",
      CreatedAt = now,
      UpdatedAt = now
    };
    db.Listings.Add(listing);

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
    db.StoreOffers.Add(offer);

    await db.SaveChangesAsync();
    return offer.Id.ToString();
  }

  private static async Task<string> CreateGuestCartWithLineAsync(HttpClient client, string offerId)
  {
    var get = await client.GetAsync("/api/cart");
    get.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    var put = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(Guid.Parse(offerId), 1))
    };
    put.Headers.Add("X-Cart-Id", cartId);

    var putRes = await client.SendAsync(put);
    putRes.StatusCode.Should().Be(HttpStatusCode.OK);

    return cartId;
  }

  private static async Task<StartCheckoutResponse> StartCheckoutAsync(HttpClient client, string cartId, string email)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId), Email: email))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    body.Should().NotBeNull();
    return body!;
  }
}
