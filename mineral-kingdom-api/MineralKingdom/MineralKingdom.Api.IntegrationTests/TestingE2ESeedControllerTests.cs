using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
    dto.AuctionListingId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"));
  }

  private sealed record E2ESeedResponse(
    Guid StoreListingId,
    Guid StoreOfferId,
    Guid AuctionListingId,
    Guid AuctionId);
}