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

public sealed class CheckoutHoldReleaseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public CheckoutHoldReleaseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Completing_hold_marks_listing_sold_and_prevents_next_checkout_for_same_offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    // Cart A starts checkout (acquires hold)
    var cartA = await CreateGuestCartWithLineAsync(client, offerId);
    var startA = await StartCheckoutAsync(client, cartA);

    // Insert matching Stripe payment row so webhook can update it (optional but nice)
    var paymentId = Guid.NewGuid();
    await InsertStripeCheckoutPaymentAsync(factory, paymentId, startA.HoldId, Guid.Parse(cartA), providerCheckoutId: "cs_test_release_complete_1");

    // Fire Stripe webhook in Testing mode
    var eventId = "evt_release_complete_1";
    var payload = StripeCheckoutSessionCompletedJson(
      holdId: startA.HoldId,
      paymentId: paymentId,
      sessionId: "cs_test_release_complete_1",
      paymentIntent: "pi_release_complete_1");

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Stripe-Event-Id", eventId);

    var hookRes = await client.SendAsync(req);
    hookRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Cart B tries to buy the same offer again -> should fail now (offer disabled / listing sold)
    var cartB = await CreateGuestCartWithLineAsync(client, offerId);

    var startBReq = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartB)))
    };
    startBReq.Headers.Add("X-Cart-Id", cartB);

    var startBRes = await client.SendAsync(startBReq);
    startBRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var err = await startBRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    err.Should().NotBeNull();
    err!.Should().ContainKey("error");
    err["error"].Should().Be("OFFER_NOT_FOUND");

    // Also assert DB state is as expected
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var offer = await db.StoreOffers.SingleAsync(o => o.Id == Guid.Parse(offerId));
      offer.IsActive.Should().BeFalse();

      var listing = await db.Listings.SingleAsync(l => l.Id == offer.ListingId);
      listing.Status.Should().Be(ListingStatuses.Sold);
      listing.QuantityAvailable.Should().Be(0);

      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == startA.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
      hold.PaymentReference.Should().Be("pi_release_complete_1");
    }
  }

  [Fact]
  public async Task Expiring_hold_releases_listing_for_next_checkout()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1100);
    var client = factory.CreateClient();

    // Cart A starts checkout (acquires hold)
    var cartA = await CreateGuestCartWithLineAsync(client, offerId);
    var startA = await StartCheckoutAsync(client, cartA);

    // Force expiry in DB (then call HeartbeatAsync to run the expiry + deactivate hold items logic)
    var now = DateTimeOffset.UtcNow;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == startA.HoldId);
      hold.ExpiresAt = now.AddMinutes(-1);
      hold.UpdatedAt = now;
      await db.SaveChangesAsync();

      var (ok, err, _) = await checkout.HeartbeatAsync(startA.HoldId, userId: null, now: now, ct: default);
      ok.Should().BeFalse();
      err.Should().Be("HOLD_EXPIRED");
    }

    // Cart B should now be able to start checkout for the same offer/listing
    var cartB = await CreateGuestCartWithLineAsync(client, offerId);

    var startBReq = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartB)))
    };
    startBReq.Headers.Add("X-Cart-Id", cartB);

    var startBRes = await client.SendAsync(startBReq);
    if (startBRes.StatusCode != HttpStatusCode.OK)
    {
      var body = await startBRes.Content.ReadAsStringAsync();
      throw new Xunit.Sdk.XunitException($"Expected 200 OK but got {(int)startBRes.StatusCode} {startBRes.StatusCode}. Body: {body}");
    }

    startBRes.StatusCode.Should().Be(HttpStatusCode.OK);


    // Defensive: offer should still be active and listing still published (we didn't complete a payment)
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var offer = await db.StoreOffers.SingleAsync(o => o.Id == Guid.Parse(offerId));
      offer.IsActive.Should().BeTrue();

      var listing = await db.Listings.SingleAsync(l => l.Id == offer.ListingId);
      listing.Status.Should().Be(ListingStatuses.Published);
      listing.QuantityAvailable.Should().BeGreaterThan(0);
    }
  }

  // ------- helpers -------

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
      Title = "Release Test Listing",
      Description = "Test",
      Status = ListingStatuses.Published,
      IsFluorescent = false,
      IsLot = false,
      QuantityTotal = 1,
      QuantityAvailable = 1,
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
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId)))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private static async Task InsertStripeCheckoutPaymentAsync(
    TestAppFactory factory,
    Guid paymentId,
    Guid holdId,
    Guid cartId,
    string providerCheckoutId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    db.CheckoutPayments.Add(new CheckoutPayment
    {
      Id = paymentId,
      HoldId = holdId,
      CartId = cartId,
      Provider = PaymentProviders.Stripe,
      Status = CheckoutPaymentStatuses.Redirected,
      AmountCents = 1000,
      CurrencyCode = "USD",
      ProviderCheckoutId = providerCheckoutId,
      CreatedAt = now,
      UpdatedAt = now
    });

    await db.SaveChangesAsync();
  }

  private static string StripeCheckoutSessionCompletedJson(Guid holdId, Guid paymentId, string sessionId, string paymentIntent)
  {
    return $$"""
{
  "id": "evt_release_payload",
  "type": "checkout.session.completed",
  "data": {
    "object": {
      "id": "{{sessionId}}",
      "payment_intent": "{{paymentIntent}}",
      "metadata": {
        "hold_id": "{{holdId}}",
        "payment_id": "{{paymentId}}"
      }
    }
  }
}
""";
  }
}
