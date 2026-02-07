using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class PayPalWebhookPaymentsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public PayPalWebhookPaymentsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task PayPal_capture_completed_webhook_completes_hold_and_sets_reference()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1200);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    var orderId = "O-TEST-ORDER-123";
    await InsertPayPalCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 1200, currency: "USD", providerCheckoutId: orderId);

    var eventId = "pp_transmission_test_1";
    var captureId = "CAPTURE_TEST_1";
    var payload = PayPalCaptureCompletedJson(orderId, captureId);

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("PAYPAL-TRANSMISSION-ID", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
      hold.CompletedAt.Should().NotBeNull();
      hold.PaymentReference.Should().Be(captureId);

      var pay = await db.CheckoutPayments.SingleAsync(p => p.Id == paymentId);
      pay.Status.Should().Be(CheckoutPaymentStatuses.Succeeded);
      pay.ProviderCheckoutId.Should().Be(orderId);
      pay.ProviderPaymentId.Should().Be(captureId);

      var evtCount = await db.PaymentWebhookEvents
        .CountAsync(e => e.Provider == PaymentProviders.PayPal && e.EventId == eventId);

      evtCount.Should().Be(1);
    }
  }

  [Fact]
  public async Task PayPal_webhook_idempotency_same_event_twice_only_processes_once()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 900);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    var orderId = "O-TEST-ORDER-999";
    await InsertPayPalCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 900, currency: "USD", providerCheckoutId: orderId);

    var eventId = "pp_transmission_idem_1";
    var captureId = "CAPTURE_IDEM_1";
    var payload = PayPalCaptureCompletedJson(orderId, captureId);

    async Task<HttpResponseMessage> PostOnceAsync()
    {
      var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
      {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
      };
      req.Headers.Add("PAYPAL-TRANSMISSION-ID", eventId);
      return await client.SendAsync(req);
    }

    var r1 = await PostOnceAsync();
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    var r2 = await PostOnceAsync();
    r2.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var count = await db.PaymentWebhookEvents
        .CountAsync(e => e.Provider == PaymentProviders.PayPal && e.EventId == eventId);

      count.Should().Be(1);
    }
  }

  [Fact]
  public async Task PayPal_webhook_completion_marks_listing_sold_disables_offer_and_deactivates_hold_items()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1200);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    var orderId = "O-TEST-ORDER-SOLD-1";
    await InsertPayPalCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 1200, currency: "USD", providerCheckoutId: orderId);

    var eventId = "pp_transmission_sold_1";
    var captureId = "CAPTURE_SOLD_1";
    var payload = PayPalCaptureCompletedJson(orderId, captureId);

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("PAYPAL-TRANSMISSION-ID", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Hold completes and reference set
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
      hold.PaymentReference.Should().Be(captureId);

      // Hold items -> source of truth for what got sold
      var holdItems = await db.CheckoutHoldItems
        .AsNoTracking()
        .Where(i => i.HoldId == start.HoldId)
        .Select(i => new { i.ListingId, i.OfferId, i.IsActive })
        .ToListAsync();

      holdItems.Should().NotBeEmpty();
      holdItems.All(x => x.IsActive == false).Should().BeTrue(); // deactivated

      var listingIds = holdItems.Select(x => x.ListingId).Distinct().ToList();
      var offerIds = holdItems.Select(x => x.OfferId).Distinct().ToList();

      // Listing(s) SOLD + qty 0
      var listings = await db.Listings.Where(l => listingIds.Contains(l.Id)).ToListAsync();
      listings.Should().NotBeEmpty();
      listings.All(l => l.Status == ListingStatuses.Sold).Should().BeTrue();
      listings.All(l => l.QuantityAvailable == 0).Should().BeTrue();

      // Offer(s) disabled
      var offers = await db.StoreOffers.Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null).ToListAsync();
      offers.Should().NotBeEmpty();
      offers.All(o => o.IsActive == false).Should().BeTrue();
    }
  }

  // ---------------- helpers ----------------

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
      Status = ListingStatuses.Published,
      IsFluorescent = false,
      IsLot = false,
      QuantityTotal = 10,
      QuantityAvailable = 10,
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
    get.StatusCode.Should().Be(HttpStatusCode.OK);

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

  private static async Task<StartCheckoutResponse> StartCheckoutAsync(HttpClient client, string cartId)
  {
    const string GuestEmail = "guest@example.com";
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId), Email: GuestEmail))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private static async Task InsertPayPalCheckoutPaymentAsync(
    TestAppFactory factory,
    Guid paymentId,
    Guid holdId,
    Guid cartId,
    int amountCents,
    string currency,
    string providerCheckoutId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var payment = new CheckoutPayment
    {
      Id = paymentId,
      HoldId = holdId,
      CartId = cartId,
      Provider = PaymentProviders.PayPal,
      Status = CheckoutPaymentStatuses.Redirected,
      AmountCents = amountCents,
      CurrencyCode = currency,
      ProviderCheckoutId = providerCheckoutId,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.CheckoutPayments.Add(payment);
    await db.SaveChangesAsync();
  }

  private static string PayPalCaptureCompletedJson(string orderId, string captureId)
  {
    return $$"""
{
  "id": "WH-TEST",
  "event_type": "PAYMENT.CAPTURE.COMPLETED",
  "resource": {
    "id": "{{captureId}}",
    "supplementary_data": {
      "related_ids": {
        "order_id": "{{orderId}}"
      }
    }
  }
}
""";
  }
}
