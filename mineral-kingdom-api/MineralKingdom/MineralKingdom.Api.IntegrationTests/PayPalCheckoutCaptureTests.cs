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

public sealed class PayPalCheckoutCaptureTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public PayPalCheckoutCaptureTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Capture_paypal_payment_sets_provider_payment_id_and_returns_ok()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1500);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var startPaymentRes = await client.PostAsJsonAsync("/api/payments/start", new StartPaymentRequest(
      HoldId: start.HoldId,
      Provider: PaymentProviders.PayPal,
      SuccessUrl: "http://localhost:3000/checkout/return",
      CancelUrl: "http://localhost:3000/checkout/return?cancelled=1"));

    startPaymentRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var startPayment = await startPaymentRes.Content.ReadFromJsonAsync<StartPaymentResponse>();
    startPayment.Should().NotBeNull();

    var captureRes = await client.PostAsync($"/api/payments/{startPayment!.PaymentId}/capture", null);
    captureRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var captureBody = await captureRes.Content.ReadFromJsonAsync<CapturePaymentResponse>();
    captureBody.Should().NotBeNull();
    captureBody!.PaymentId.Should().Be(startPayment.PaymentId);
    captureBody.Provider.Should().Be(PaymentProviders.PayPal);
    captureBody.PaymentStatus.Should().Be(CheckoutPaymentStatuses.Redirected);
    captureBody.ProviderPaymentId.Should().NotBeNullOrWhiteSpace();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var payment = await db.CheckoutPayments.SingleAsync(x => x.Id == startPayment.PaymentId);
    payment.Provider.Should().Be(PaymentProviders.PayPal);
    payment.ProviderCheckoutId.Should().NotBeNullOrWhiteSpace();
    payment.ProviderPaymentId.Should().NotBeNullOrWhiteSpace();
    payment.Status.Should().Be(CheckoutPaymentStatuses.Redirected);
  }

  [Fact]
  public async Task Capture_stripe_payment_returns_bad_request()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1500);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var startPaymentRes = await client.PostAsJsonAsync("/api/payments/start", new StartPaymentRequest(
      HoldId: start.HoldId,
      Provider: PaymentProviders.Stripe,
      SuccessUrl: "http://localhost:3000/checkout/return",
      CancelUrl: "http://localhost:3000/checkout/return?cancelled=1"));

    startPaymentRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var startPayment = await startPaymentRes.Content.ReadFromJsonAsync<StartPaymentResponse>();
    startPayment.Should().NotBeNull();

    var captureRes = await client.PostAsync($"/api/payments/{startPayment!.PaymentId}/capture", null);
    captureRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await captureRes.Content.ReadFromJsonAsync<ErrorDto>();
    body.Should().NotBeNull();
    body!.Error.Should().Be("PROVIDER_CAPTURE_NOT_SUPPORTED");
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
      Title = "PayPal Capture Test Listing",
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
    const string GuestEmail = "guest@example.com";

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(Guid.Parse(cartId), GuestEmail))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private sealed record ErrorDto(string Error);
}