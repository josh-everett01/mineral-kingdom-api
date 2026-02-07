using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CheckoutHoldsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CheckoutHoldsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Guest_cart_reuses_X_Cart_Id_and_can_add_line_then_get()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);

    var client = factory.CreateClient();

    // Step 1: GET cart (no header) => server creates cart and returns X-Cart-Id
    var get1 = await client.GetAsync("/api/cart");
    get1.StatusCode.Should().Be(HttpStatusCode.OK);

    get1.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    // Step 2: PUT line using X-Cart-Id
    var put = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(Guid.Parse(offerId), 1))
    };
    put.Headers.Add("X-Cart-Id", cartId);

    var putRes = await client.SendAsync(put);
    putRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Step 3: GET cart again using same X-Cart-Id => should show line
    var get2 = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
    get2.Headers.Add("X-Cart-Id", cartId);

    var get2Res = await client.SendAsync(get2);
    get2Res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await get2Res.Content.ReadFromJsonAsync<CartDto>();
    dto.Should().NotBeNull();
    dto!.CartId.ToString().Should().Be(cartId);
    dto.Lines.Should().ContainSingle(x => x.OfferId == Guid.Parse(offerId) && x.Quantity == 1);
  }

  [Fact]
  public async Task Start_checkout_returns_same_active_hold_if_not_expired()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    // Create cart + add line
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);

    // Start checkout twice => same hold id (since active + not expired)
    var start1 = await StartCheckoutAsync(client, cartId);
    var start2 = await StartCheckoutAsync(client, cartId);

    start2.HoldId.Should().Be(start1.HoldId);
  }

  [Fact]
  public async Task Client_complete_does_not_checkout_cart_or_complete_hold()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var hold = await StartCheckoutAsync(client, cartId);

    // Client calls complete after provider redirect
    var res = await CompleteCheckoutAsync(client, cartId, hold.HoldId, "client_ref_123");
    res.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Verify: cart is still ACTIVE and hold is still ACTIVE
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var cart = await db.Carts.SingleAsync(c => c.Id == Guid.Parse(cartId));
    cart.Status.Should().Be(CartStatuses.Active);

    var dbHold = await db.CheckoutHolds.SingleAsync(h => h.Id == hold.HoldId);
    dbHold.Status.Should().Be(CheckoutHoldStatuses.Active);
    dbHold.CompletedAt.Should().BeNull();
    dbHold.PaymentReference.Should().BeNull();

    // Optional: if you added fields, verify we recorded return
    dbHold.ClientReturnedAt.Should().NotBeNull();
    dbHold.ClientReturnReference.Should().Be("client_ref_123");
  }

  [Fact]
  public async Task First_successful_payment_wins_when_confirmed_by_webhook_only()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);

    // Hold A via API
    var a = await StartCheckoutAsync(client, cartId);

    // Hold B inserted directly to simulate race (2 active holds exist)
    var bHoldId = await InsertSecondActiveHoldAsync(factory, Guid.Parse(cartId));

    // Webhook confirms B: should succeed
    using (var scope = factory.Services.CreateScope())
    {
      var checkout = scope.ServiceProvider.GetRequiredService<MineralKingdom.Infrastructure.Store.CheckoutService>();
      var (okB, errB) = await checkout.ConfirmPaidFromWebhookAsync(bHoldId, "wh_pay_B", DateTimeOffset.UtcNow, CancellationToken.None);
      okB.Should().BeTrue(errB);
    }

    // Webhook confirms A: should fail due to "first paid wins"
    using (var scope = factory.Services.CreateScope())
    {
      var checkout = scope.ServiceProvider.GetRequiredService<MineralKingdom.Infrastructure.Store.CheckoutService>();
      var (okA, errA) = await checkout.ConfirmPaidFromWebhookAsync(a.HoldId, "wh_pay_A", DateTimeOffset.UtcNow, CancellationToken.None);

      okA.Should().BeFalse();
      errA.Should().Be("PAYMENT_ALREADY_COMPLETED");
    }
  }


  // ---------------- helpers ----------------

  private static async Task<Guid> InsertSecondActiveHoldAsync(TestAppFactory factory, Guid cartId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var hold = new CheckoutHold
    {
      Id = Guid.NewGuid(),
      CartId = cartId,
      UserId = null, // guest
      Status = CheckoutHoldStatuses.Active,
      ExpiresAt = now.AddMinutes(5),
      CreatedAt = now,
      UpdatedAt = now
    };

    db.CheckoutHolds.Add(hold);
    await db.SaveChangesAsync();

    return hold.Id;
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

    // Minimal listing row (whatever your listing requires might differ; if required, adjust this seed)
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
    // Create cart
    var get = await client.GetAsync("/api/cart");
    get.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    // Add line
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

    var body = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    body.Should().NotBeNull();
    return body!;
  }

  private static async Task<HttpResponseMessage> CompleteCheckoutAsync(HttpClient client, string cartId, Guid holdId, string paymentRef)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/complete")
    {
      Content = JsonContent.Create(new CompleteCheckoutRequest(holdId, paymentRef))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    return await client.SendAsync(req);
  }

  private static async Task ExpireHoldAsync(TestAppFactory factory, Guid holdId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var hold = await db.CheckoutHolds.SingleAsync(x => x.Id == holdId);
    hold.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10);
    hold.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
  }
}
