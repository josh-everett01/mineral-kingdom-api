using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class TestingE2ESeedControllerTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public TestingE2ESeedControllerTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Seed_endpoint_returns_deterministic_fixture_ids()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var res = await client.PostAsync("/api/testing/e2e/seed", content: null);

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<E2ESeedResponse>();
    dto.Should().NotBeNull();

    dto!.StoreListingId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"));
    dto.StoreOfferId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"));
    dto.StoreListing2Id.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"));
    dto.StoreOffer2Id.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6"));
    dto.AuctionListingId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"));
    dto.AuctionId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3"));
  }

  [Fact]
  public async Task Seed_endpoint_resets_active_checkout_hold_for_seeded_store_listing()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var seedRes = await client.PostAsync("/api/testing/e2e/seed", content: null);
    seedRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var cartRes = await client.GetAsync("/api/cart");
    cartRes.StatusCode.Should().Be(HttpStatusCode.OK);
    var cartId = cartRes.Headers.GetValues("X-Cart-Id").Single();

    var addReq = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"),
        1))
    };
    addReq.Headers.Add("X-Cart-Id", cartId);

    var addRes = await client.SendAsync(addReq);
    addRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var startReq = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(
        CartId: null,
        Email: "guest@example.com"))
    };
    startReq.Headers.Add("X-Cart-Id", cartId);

    var startRes = await client.SendAsync(startReq);
    startRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var reseedRes = await client.PostAsync("/api/testing/e2e/seed", content: null);
    reseedRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var cartRes2 = await client.GetAsync("/api/cart");
    cartRes2.StatusCode.Should().Be(HttpStatusCode.OK);
    var cartId2 = cartRes2.Headers.GetValues("X-Cart-Id").Single();

    var addReq2 = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"),
        1))
    };
    addReq2.Headers.Add("X-Cart-Id", cartId2);

    var addRes2 = await client.SendAsync(addReq2);
    addRes2.StatusCode.Should().Be(HttpStatusCode.OK);

    var startReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(
        CartId: null,
        Email: "guest2@example.com"))
    };
    startReq2.Headers.Add("X-Cart-Id", cartId2);

    var startRes2 = await client.SendAsync(startReq2);
    startRes2.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var holdItems = await db.CheckoutHoldItems
      .Where(x => x.ListingId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"))
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync();

    holdItems.Should().NotBeEmpty();
    holdItems.Count(x => x.IsActive).Should().Be(1);
  }

  [Fact]
  public async Task Seed_endpoint_resets_active_checkout_hold_for_second_seeded_store_listing()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var seedRes = await client.PostAsync("/api/testing/e2e/seed", content: null);
    seedRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var cartRes = await client.GetAsync("/api/cart");
    cartRes.StatusCode.Should().Be(HttpStatusCode.OK);
    var cartId = cartRes.Headers.GetValues("X-Cart-Id").Single();

    var addReq = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6"),
        1))
    };
    addReq.Headers.Add("X-Cart-Id", cartId);

    var addRes = await client.SendAsync(addReq);
    addRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var startReq = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(
        CartId: null,
        Email: "guest@example.com"))
    };
    startReq.Headers.Add("X-Cart-Id", cartId);

    var startRes = await client.SendAsync(startReq);
    startRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var reseedRes = await client.PostAsync("/api/testing/e2e/seed", content: null);
    reseedRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var cartRes2 = await client.GetAsync("/api/cart");
    cartRes2.StatusCode.Should().Be(HttpStatusCode.OK);
    var cartId2 = cartRes2.Headers.GetValues("X-Cart-Id").Single();

    var addReq2 = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6"),
        1))
    };
    addReq2.Headers.Add("X-Cart-Id", cartId2);

    var addRes2 = await client.SendAsync(addReq2);
    addRes2.StatusCode.Should().Be(HttpStatusCode.OK);

    var startReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(
        CartId: null,
        Email: "guest2@example.com"))
    };
    startReq2.Headers.Add("X-Cart-Id", cartId2);

    var startRes2 = await client.SendAsync(startReq2);
    startRes2.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var holdItems = await db.CheckoutHoldItems
      .Where(x => x.ListingId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"))
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync();

    holdItems.Should().NotBeEmpty();
    holdItems.Count(x => x.IsActive).Should().Be(1);
  }

  private sealed record E2ESeedResponse(
    Guid StoreListingId,
    Guid StoreOfferId,
    Guid StoreListing2Id,
    Guid StoreOffer2Id,
    Guid AuctionListingId,
    Guid AuctionId);
}