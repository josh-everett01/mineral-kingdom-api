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

public sealed class PaymentConfirmationLookupTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public PaymentConfirmationLookupTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Confirmation_returns_unconfirmed_before_webhook_creates_order()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);
    using var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentStart = await StartPaymentAsync(client, start.HoldId, PaymentProviders.Stripe);

    var res = await client.GetAsync($"/api/payments/{paymentStart.PaymentId}/confirmation");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<PaymentConfirmationResponse>();
    dto.Should().NotBeNull();
    dto!.PaymentId.Should().Be(paymentStart.PaymentId);
    dto.PaymentStatus.Should().Be(CheckoutPaymentStatuses.Redirected);
    dto.IsConfirmed.Should().BeFalse();
    dto.OrderId.Should().BeNull();
    dto.OrderStatus.Should().BeNull();
  }

  [Fact]
  public async Task Confirmation_returns_order_after_stripe_webhook_confirms_payment()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1200);
    using var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);
    var paymentStart = await StartPaymentAsync(client, start.HoldId, PaymentProviders.Stripe);

    var eventId = "evt_payment_confirmation_1";
    var paymentIntentId = "pi_payment_confirmation_1";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentStart.PaymentId, paymentIntentId);

    var webhookReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    webhookReq.Headers.Add("X-Stripe-Event-Id", eventId);

    var webhookRes = await client.SendAsync(webhookReq);
    webhookRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var res = await client.GetAsync($"/api/payments/{paymentStart.PaymentId}/confirmation");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<PaymentConfirmationResponse>();
    dto.Should().NotBeNull();
    dto!.PaymentId.Should().Be(paymentStart.PaymentId);
    dto.PaymentStatus.Should().Be(CheckoutPaymentStatuses.Succeeded);
    dto.IsConfirmed.Should().BeTrue();
    dto.OrderId.Should().NotBeNull();
    dto.OrderNumber.Should().StartWith("MK-");
    dto.OrderStatus.Should().Be("READY_TO_FULFILL");
    dto.GuestEmail.Should().Be("guest@example.com");
  }

  [Fact]
  public async Task Confirmation_returns_not_found_for_unknown_payment()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    var res = await client.GetAsync($"/api/payments/{Guid.NewGuid()}/confirmation");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

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
    const string guestEmail = "guest@example.com";
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId), Email: guestEmail))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private static async Task<StartPaymentResponse> StartPaymentAsync(HttpClient client, Guid holdId, string provider)
  {
    var res = await client.PostAsJsonAsync("/api/payments/start", new StartPaymentRequest(
      HoldId: holdId,
      Provider: provider,
      SuccessUrl: "http://localhost:3000/checkout/return?provider=stripe",
      CancelUrl: "http://localhost:3000/checkout/return?cancelled=1"
    ));

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    var dto = await res.Content.ReadFromJsonAsync<StartPaymentResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private static string StripeCheckoutSessionCompletedJson(Guid holdId, Guid paymentId, string paymentIntentId) =>
    $$"""
    {
      "type": "checkout.session.completed",
      "data": {
        "object": {
          "id": "cs_test_payment_confirmation",
          "payment_intent": "{{paymentIntentId}}",
          "metadata": {
            "hold_id": "{{holdId}}",
            "payment_id": "{{paymentId}}"
          }
        }
      }
    }
    """;
}
