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

public sealed class CartNoticeReconciliationTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CartNoticeReconciliationTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Webhook_confirmed_purchase_removes_sold_offer_from_other_active_carts()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var offerId = await SeedStoreOfferAsync(factory, now);

    var clientA = factory.CreateClient();
    var clientB = factory.CreateClient();

    var cartAId = await CreateGuestCartWithLineAsync(clientA, offerId);
    var cartBId = await CreateGuestCartWithLineAsync(clientB, offerId);

    var holdId = await StartGuestCheckoutAsync(clientA, cartAId, "buyer@example.com");
    await ConfirmPaidAsync(factory, holdId, "test-payment-ref", now);

    var cartB = await GetCartAsync(clientB, cartBId);
    cartB.Should().NotBeNull();
    cartB!.Lines.Should().BeEmpty();
  }

  [Fact]
  public async Task Webhook_confirmed_purchase_creates_cart_notice_for_affected_cart()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var offerId = await SeedStoreOfferAsync(factory, now);

    var clientA = factory.CreateClient();
    var clientB = factory.CreateClient();

    var cartAId = await CreateGuestCartWithLineAsync(clientA, offerId);
    var cartBId = await CreateGuestCartWithLineAsync(clientB, offerId);

    var holdId = await StartGuestCheckoutAsync(clientA, cartAId, "buyer@example.com");
    await ConfirmPaidAsync(factory, holdId, "test-payment-ref", now);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var notices = await db.CartNotices
      .Where(x => x.CartId == Guid.Parse(cartBId) && x.DismissedAt == null)
      .ToListAsync();

    notices.Should().HaveCount(1);
    notices[0].Type.Should().Be(CartNoticeTypes.ItemRemovedSold);
    notices[0].OfferId.Should().Be(Guid.Parse(offerId));
    notices[0].Message.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task Cart_get_returns_active_notices()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var offerId = await SeedStoreOfferAsync(factory, now);

    var clientA = factory.CreateClient();
    var clientB = factory.CreateClient();

    var cartAId = await CreateGuestCartWithLineAsync(clientA, offerId);
    var cartBId = await CreateGuestCartWithLineAsync(clientB, offerId);

    var holdId = await StartGuestCheckoutAsync(clientA, cartAId, "buyer@example.com");
    await ConfirmPaidAsync(factory, holdId, "test-payment-ref", now);

    var cartB = await GetCartAsync(clientB, cartBId);
    cartB.Should().NotBeNull();
    cartB!.Notices.Should().HaveCount(1);
    cartB.Notices[0].Type.Should().Be(CartNoticeTypes.ItemRemovedSold);
    cartB.Notices[0].DismissedAt.Should().BeNull();
  }

  [Fact]
  public async Task Dismiss_notice_marks_notice_dismissed_and_hides_it_from_cart_get()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var offerId = await SeedStoreOfferAsync(factory, now);

    var clientA = factory.CreateClient();
    var clientB = factory.CreateClient();

    var cartAId = await CreateGuestCartWithLineAsync(clientA, offerId);
    var cartBId = await CreateGuestCartWithLineAsync(clientB, offerId);

    var holdId = await StartGuestCheckoutAsync(clientA, cartAId, "buyer@example.com");
    await ConfirmPaidAsync(factory, holdId, "test-payment-ref", now);

    var cartBeforeDismiss = await GetCartAsync(clientB, cartBId);
    cartBeforeDismiss!.Notices.Should().HaveCount(1);

    var noticeId = cartBeforeDismiss.Notices[0].Id;

    var dismissRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/cart/notices/{noticeId}/dismiss");
    dismissRequest.Headers.Add("X-Cart-Id", cartBId);

    var dismissResponse = await clientB.SendAsync(dismissRequest);
    dismissResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var dismissDto = await dismissResponse.Content.ReadFromJsonAsync<DismissCartNoticeResponse>();
    dismissDto.Should().NotBeNull();
    dismissDto!.Dismissed.Should().BeTrue();

    var cartAfterDismiss = await GetCartAsync(clientB, cartBId);
    cartAfterDismiss!.Notices.Should().BeEmpty();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var notice = await db.CartNotices.SingleAsync(x => x.Id == noticeId);
    notice.DismissedAt.Should().NotBeNull();
  }

  [Fact]
  public async Task Buyer_cart_does_not_receive_competing_cart_notice()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var offerId = await SeedStoreOfferAsync(factory, now);

    var clientA = factory.CreateClient();
    var clientB = factory.CreateClient();

    var cartAId = await CreateGuestCartWithLineAsync(clientA, offerId);
    await CreateGuestCartWithLineAsync(clientB, offerId);

    var holdId = await StartGuestCheckoutAsync(clientA, cartAId, "buyer@example.com");
    await ConfirmPaidAsync(factory, holdId, "test-payment-ref", now);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var buyerNotices = await db.CartNotices
      .Where(x => x.CartId == Guid.Parse(cartAId))
      .ToListAsync();

    buyerNotices.Should().BeEmpty();
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<string> SeedStoreOfferAsync(TestAppFactory factory, DateTimeOffset now)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Reconciliation Test Listing",
      Description = "Test listing",
      Status = "PUBLISHED",
      QuantityTotal = 1,
      QuantityAvailable = 1,
      IsFluorescent = false,
      IsLot = false,
      CreatedAt = now,
      UpdatedAt = now
    };

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      PriceCents = 10000,
      DiscountType = DiscountTypes.None,
      IsActive = true,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);
    db.StoreOffers.Add(offer);
    await db.SaveChangesAsync();

    return offer.Id.ToString();
  }

  private static async Task<string> CreateGuestCartWithLineAsync(HttpClient client, string offerId)
  {
    var getCart = await client.GetAsync("/api/cart");
    getCart.StatusCode.Should().Be(HttpStatusCode.OK);

    getCart.Headers.TryGetValues("X-Cart-Id", out var headerValues).Should().BeTrue();
    var cartId = headerValues!.Single();

    var request = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(Guid.Parse(offerId), 1))
    };
    request.Headers.Add("X-Cart-Id", cartId);

    var putResponse = await client.SendAsync(request);
    putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    return cartId;
  }

  private static async Task<Guid> StartGuestCheckoutAsync(HttpClient client, string cartId, string email)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(Guid.Parse(cartId), email))
    };
    request.Headers.Add("X-Cart-Id", cartId);

    var response = await client.SendAsync(request);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await response.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();

    return dto!.HoldId;
  }

  private static async Task ConfirmPaidAsync(TestAppFactory factory, Guid holdId, string paymentReference, DateTimeOffset now)
  {
    using var scope = factory.Services.CreateScope();
    var checkout = scope.ServiceProvider.GetRequiredService<MineralKingdom.Infrastructure.Store.CheckoutService>();

    var result = await checkout.ConfirmPaidFromWebhookAsync(
      holdId,
      paymentReference,
      now,
      CancellationToken.None);

    result.Ok.Should().BeTrue();
  }

  private static async Task<CartDto?> GetCartAsync(HttpClient client, string cartId)
  {
    var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
    request.Headers.Add("X-Cart-Id", cartId);

    var response = await client.SendAsync(request);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    return await response.Content.ReadFromJsonAsync<CartDto>();
  }
}