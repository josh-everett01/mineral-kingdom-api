using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CheckoutPaymentSseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CheckoutPaymentSseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Checkout_payment_sse_emits_initial_snapshot_before_confirmation()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);
    using var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);
    var paymentStart = await StartPaymentAsync(client, start.HoldId, PaymentProviders.Stripe);

    using var req = new HttpRequestMessage(
      HttpMethod.Get,
      $"/api/checkout-payments/{paymentStart.PaymentId}/events");

    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();
    var payload = await ReadUntilContainsAsync(stream, paymentStart.PaymentId.ToString(), TimeSpan.FromSeconds(3));

    payload.Should().Contain("event: snapshot");
    payload.Should().Contain(paymentStart.PaymentId.ToString());
    payload.Should().Contain(CheckoutPaymentStatuses.Redirected);
    payload.Should().Contain(start.HoldId.ToString());
  }

  [Fact]
  public async Task Checkout_payment_sse_emits_order_link_after_webhook_confirmation()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1200);
    using var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);
    var paymentStart = await StartPaymentAsync(client, start.HoldId, PaymentProviders.Stripe);

    using var req = new HttpRequestMessage(
      HttpMethod.Get,
      $"/api/checkout-payments/{paymentStart.PaymentId}/events");

    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();

    var initial = await ReadUntilContainsAsync(stream, paymentStart.PaymentId.ToString(), TimeSpan.FromSeconds(3));
    initial.Should().Contain("event: snapshot");
    initial.Should().Contain(CheckoutPaymentStatuses.Redirected);

    var eventId = "evt_checkout_payment_sse_1";
    var paymentIntentId = "pi_checkout_payment_sse_1";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentStart.PaymentId, paymentIntentId);

    var webhookReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };
    webhookReq.Headers.Add("X-Stripe-Event-Id", eventId);

    var webhookRes = await client.SendAsync(webhookReq);
    webhookRes.StatusCode.Should().Be(HttpStatusCode.OK);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var orderId = await db.Orders
      .AsNoTracking()
      .Where(x => x.CheckoutHoldId == start.HoldId)
      .Select(x => x.Id)
      .SingleAsync();

    var updated = await ReadUntilContainsAsync(stream, orderId.ToString(), TimeSpan.FromSeconds(5));

    updated.Should().Contain("event: snapshot");
    updated.Should().Contain(paymentStart.PaymentId.ToString());
    updated.Should().Contain(CheckoutPaymentStatuses.Succeeded);
    updated.Should().Contain(orderId.ToString());
  }

  [Fact]
  public async Task Checkout_payment_confirmation_lookup_matches_sse_terminal_state()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1500);
    using var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);
    var paymentStart = await StartPaymentAsync(client, start.HoldId, PaymentProviders.Stripe);

    var eventId = "evt_checkout_payment_sse_2";
    var paymentIntentId = "pi_checkout_payment_sse_2";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentStart.PaymentId, paymentIntentId);

    var webhookReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };
    webhookReq.Headers.Add("X-Stripe-Event-Id", eventId);

    var webhookRes = await client.SendAsync(webhookReq);
    webhookRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var confirmationRes = await client.GetAsync($"/api/payments/{paymentStart.PaymentId}/confirmation");
    confirmationRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var confirmation = await confirmationRes.Content.ReadFromJsonAsync<PaymentConfirmationResponse>();
    confirmation.Should().NotBeNull();
    confirmation!.PaymentId.Should().Be(paymentStart.PaymentId);
    confirmation.PaymentStatus.Should().Be(CheckoutPaymentStatuses.Succeeded);
    confirmation.IsConfirmed.Should().BeTrue();
    confirmation.OrderId.Should().NotBeNull();

    using var sseReq = new HttpRequestMessage(
      HttpMethod.Get,
      $"/api/checkout-payments/{paymentStart.PaymentId}/events");

    var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead);
    sseRes.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var stream = await sseRes.Content.ReadAsStreamAsync();
    var ssePayload = await ReadUntilContainsAsync(stream, confirmation.OrderId!.Value.ToString(), TimeSpan.FromSeconds(3));

    ssePayload.Should().Contain("event: snapshot");
    ssePayload.Should().Contain(CheckoutPaymentStatuses.Succeeded);
    ssePayload.Should().Contain(confirmation.OrderId.Value.ToString());
  }

  private static async Task<string> ReadUntilContainsAsync(
    Stream stream,
    string expected,
    TimeSpan timeout)
  {
    var sb = new StringBuilder();
    var buffer = new byte[1024];
    using var timeoutCts = new CancellationTokenSource(timeout);

    while (!timeoutCts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
      if (n <= 0)
        break;

      sb.Append(Encoding.UTF8.GetString(buffer, 0, n));

      if (sb.ToString().Contains(expected, StringComparison.OrdinalIgnoreCase))
        break;
    }

    return sb.ToString();
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
          "id": "cs_test_checkout_payment_sse",
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