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

public sealed class CheckoutActiveResetTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public CheckoutActiveResetTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Active_returns_current_guest_hold_and_guest_email()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId, "guest@example.com");

    var req = new HttpRequestMessage(HttpMethod.Get, "/api/checkout/active");
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ActiveCheckoutResponse>();
    dto.Should().NotBeNull();
    dto!.Active.Should().BeTrue();
    dto.CartId.Should().Be(Guid.Parse(cartId));
    dto.HoldId.Should().Be(start.HoldId);
    dto.GuestEmail.Should().Be("guest@example.com");
    dto.Status.Should().Be(CheckoutHoldStatuses.Active);
    dto.ExpiresAt.Should().NotBeNull();
  }

  [Fact]
  public async Task Reset_expires_active_hold_and_allows_restart_with_different_email()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId, "guest@example.com");

    var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/reset");
    resetReq.Headers.Add("X-Cart-Id", cartId);

    var resetRes = await client.SendAsync(resetReq);
    resetRes.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Expired);

      var activeItems = await db.CheckoutHoldItems
        .Where(i => i.HoldId == start.HoldId && i.IsActive)
        .ToListAsync();

      activeItems.Should().BeEmpty();
    }

    var restarted = await StartCheckoutAsync(client, cartId, "different@example.com");
    restarted.HoldId.Should().NotBe(start.HoldId);
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
      Title = "Active Reset Test Listing",
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

  private static async Task<StartCheckoutResponse> StartCheckoutAsync(HttpClient client, string cartId, string email)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(Guid.Parse(cartId), email))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }
}