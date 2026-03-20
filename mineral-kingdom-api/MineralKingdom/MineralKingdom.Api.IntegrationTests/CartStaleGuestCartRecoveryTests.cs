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

public sealed class CartStaleGuestCartRecoveryTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CartStaleGuestCartRecoveryTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_with_checked_out_guest_cart_id_returns_new_active_cart()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var staleCartId = await SeedCheckedOutGuestCartAsync(factory);

    var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
    req.Headers.Add("X-Cart-Id", staleCartId.ToString());

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    res.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var returnedCartIdRaw = values!.Single();
    var returnedCartId = Guid.Parse(returnedCartIdRaw);

    returnedCartId.Should().NotBe(staleCartId);

    var body = await res.Content.ReadFromJsonAsync<CartDto>();
    body.Should().NotBeNull();
    body!.CartId.Should().Be(returnedCartId);
    body.Status.Should().Be(CartStatuses.Active);
    body.Lines.Should().BeEmpty();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var staleCart = await db.Carts.SingleAsync(x => x.Id == staleCartId);
    staleCart.Status.Should().Be(CartStatuses.CheckedOut);

    var freshCart = await db.Carts.SingleAsync(x => x.Id == returnedCartId);
    freshCart.Status.Should().Be(CartStatuses.Active);
    freshCart.UserId.Should().BeNull();
  }

  [Fact]
  public async Task Put_lines_with_checked_out_guest_cart_id_creates_new_active_cart_and_adds_line()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, "Recovered Cart Listing", 12345);
    var staleCartId = await SeedCheckedOutGuestCartAsync(factory);

    var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(Guid.Parse(offerId), 1))
    };
    req.Headers.Add("X-Cart-Id", staleCartId.ToString());

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    res.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var returnedCartIdRaw = values!.Single();
    var returnedCartId = Guid.Parse(returnedCartIdRaw);

    returnedCartId.Should().NotBe(staleCartId);

    var body = await res.Content.ReadFromJsonAsync<CartDto>();
    body.Should().NotBeNull();
    body!.CartId.Should().Be(returnedCartId);
    body.Status.Should().Be(CartStatuses.Active);
    body.Lines.Should().HaveCount(1);
    body.Lines[0].OfferId.Should().Be(Guid.Parse(offerId));

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var staleCart = await db.Carts.SingleAsync(x => x.Id == staleCartId);
    staleCart.Status.Should().Be(CartStatuses.CheckedOut);

    var freshCart = await db.Carts
      .Include(x => x.Lines)
      .SingleAsync(x => x.Id == returnedCartId);

    freshCart.Status.Should().Be(CartStatuses.Active);
    freshCart.Lines.Should().HaveCount(1);
    freshCart.Lines.Single().OfferId.Should().Be(Guid.Parse(offerId));
  }

  [Fact]
  public async Task Delete_line_with_checked_out_guest_cart_id_returns_fresh_active_cart_instead_of_cart_not_active()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var staleCartId = await SeedCheckedOutGuestCartAsync(factory);
    var randomOfferId = Guid.NewGuid();

    var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/cart/lines/{randomOfferId}");
    req.Headers.Add("X-Cart-Id", staleCartId.ToString());

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    res.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var returnedCartIdRaw = values!.Single();
    var returnedCartId = Guid.Parse(returnedCartIdRaw);

    returnedCartId.Should().NotBe(staleCartId);

    var body = await res.Content.ReadFromJsonAsync<CartDto>();
    body.Should().NotBeNull();
    body!.CartId.Should().Be(returnedCartId);
    body.Status.Should().Be(CartStatuses.Active);
    body.Lines.Should().BeEmpty();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var staleCart = await db.Carts.SingleAsync(x => x.Id == staleCartId);
    staleCart.Status.Should().Be(CartStatuses.CheckedOut);

    var freshCart = await db.Carts.SingleAsync(x => x.Id == returnedCartId);
    freshCart.Status.Should().Be(CartStatuses.Active);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<string> SeedOfferAsync(TestAppFactory factory, string title, int priceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = title,
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

  private static async Task<Guid> SeedCheckedOutGuestCartAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var cart = new Cart
    {
      Id = Guid.NewGuid(),
      UserId = null,
      Status = CartStatuses.CheckedOut,
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-5)
    };

    db.Carts.Add(cart);
    await db.SaveChangesAsync();

    return cart.Id;
  }
}