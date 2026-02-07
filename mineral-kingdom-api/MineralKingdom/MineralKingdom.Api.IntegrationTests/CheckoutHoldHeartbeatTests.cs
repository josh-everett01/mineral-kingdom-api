using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CheckoutHoldHeartbeatTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public CheckoutHoldHeartbeatTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Heartbeat_extends_expiresAt_but_caps_at_createdAt_plus_max()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    DateTimeOffset createdAt;
    int initialMinutes;
    int maxMinutes;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // TODO: replace CheckoutOptions with your actual options type
      var opts = scope.ServiceProvider.GetRequiredService<IOptions<CheckoutOptions>>().Value;
      initialMinutes = opts.HoldInitialMinutes;
      maxMinutes = opts.HoldMaxMinutes;

      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);

      // Force CreatedAt close to the max window so the next heartbeat is capped
      createdAt = DateTimeOffset.UtcNow.AddMinutes(-(maxMinutes - 1));
      hold.CreatedAt = createdAt;
      hold.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1); // ensure active
      hold.UpdatedAt = DateTimeOffset.UtcNow;

      await db.SaveChangesAsync();
    }

    var hbRes = await client.PostAsJsonAsync("/api/checkout/heartbeat", new CheckoutHeartbeatRequest(start.HoldId));
    hbRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await hbRes.Content.ReadFromJsonAsync<CheckoutHeartbeatResponse>();
    dto.Should().NotBeNull();

    // Expected: ExpiresAt = min(now + initial, createdAt + max)
    var now = DateTimeOffset.UtcNow;
    var expected = Min(now.AddMinutes(initialMinutes), createdAt.AddMinutes(maxMinutes));

    dto!.ExpiresAt.Should().BeCloseTo(expected, precision: TimeSpan.FromSeconds(10));
  }

  private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

  [Fact]
  public async Task Heartbeat_on_expired_hold_returns_hold_expired()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    // Force expiry in DB
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);

      hold.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      hold.UpdatedAt = DateTimeOffset.UtcNow;

      await db.SaveChangesAsync();
    }

    var hbRes = await client.PostAsJsonAsync("/api/checkout/heartbeat", new CheckoutHeartbeatRequest(start.HoldId));
    hbRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await hbRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body.Should().NotBeNull();
    body!["error"].Should().Be("HOLD_EXPIRED");
  }

  // --- helpers (same patterns) ---
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
      Title = "Heartbeat Test Listing",
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
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId), Email: GuestEmail))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }
}