using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CheckoutExtendTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public CheckoutExtendTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Extend_returns_bad_request_when_called_too_early()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var started = await StartCheckoutAsync(client, cartId, "guest@example.com");

    var res = await client.PostAsJsonAsync("/api/checkout/extend", new ExtendCheckoutRequest(started.HoldId));
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<ErrorDto>();
    body.Should().NotBeNull();
    body!.Error.Should().Be("TOO_EARLY_TO_EXTEND");
  }

  [Fact]
  public async Task Extend_succeeds_when_hold_is_within_threshold_and_below_limit()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var started = await StartCheckoutAsync(client, cartId, "guest@example.com");

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == started.HoldId);
      hold.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
      await db.SaveChangesAsync();
    }

    var res = await client.PostAsJsonAsync("/api/checkout/extend", new ExtendCheckoutRequest(started.HoldId));
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ExtendCheckoutResponse>();
    dto.Should().NotBeNull();
    dto!.HoldId.Should().Be(started.HoldId);
    dto.ExtensionCount.Should().Be(1);
    dto.MaxExtensions.Should().Be(2);
  }

  [Fact]
  public async Task Extend_returns_limit_reached_when_max_extensions_hit()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, 1000);

    var client = factory.CreateClient();
    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var started = await StartCheckoutAsync(client, cartId, "guest@example.com");

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == started.HoldId);
      hold.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
      hold.ExtensionCount = 2;
      await db.SaveChangesAsync();
    }

    var res = await client.PostAsJsonAsync("/api/checkout/extend", new ExtendCheckoutRequest(started.HoldId));
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<ErrorDto>();
    body.Should().NotBeNull();
    body!.Error.Should().Be("EXTENSION_LIMIT_REACHED");
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
      Title = "Extend Test Listing",
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

  private sealed record ErrorDto(string Error);
}