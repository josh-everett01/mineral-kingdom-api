using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionRealtimeSnapshotTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AuctionRealtimeSnapshotTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task GetSnapshot_returns_public_safe_fields_and_computes_minimumNextBid()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var utc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    Guid auctionId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var a = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = Guid.NewGuid(),
        Status = AuctionStatuses.Live,
        CreatedAt = now,
        UpdatedAt = now,
        BidCount = 1,
        CloseTime = utc.AddMinutes(10),
        ClosingWindowEnd = null,
        CurrentPriceCents = 1100,
        StartTime = utc.AddMinutes(-5),
        StartingPriceCents = 1000,
        ReserveMet = false,
        ReservePriceCents = null // ensures ReserveMet should be null in response
      };

      db.Auctions.Add(a);
      await db.SaveChangesAsync();

      auctionId = a.Id;
    }

    var res = await client.GetAsync($"/api/auctions/{auctionId}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var snap = await res.Content.ReadFromJsonAsync<AuctionRealtimeSnapshot>();
    snap.Should().NotBeNull();

    snap!.AuctionId.Should().Be(auctionId);
    snap.CurrentPriceCents.Should().Be(1100);
    snap.BidCount.Should().Be(1);
    snap.ReserveMet.Should().BeNull(); // no reserve price => null
    snap.Status.Should().Be(AuctionStatuses.Live);
    snap.ClosingWindowEnd.Should().BeNull();
    snap.MinimumNextBidCents.Should().BeGreaterThan(1100); // should be min-to-beat(current)
  }
}