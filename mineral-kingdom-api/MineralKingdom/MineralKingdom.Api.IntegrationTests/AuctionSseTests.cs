using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionSseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AuctionSseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Sse_emits_initial_snapshot()
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
        BidCount = 0,
        CloseTime = utc.AddMinutes(30),
        StartTime = utc.AddMinutes(-5),
        CurrentPriceCents = 1000,
        StartingPriceCents = 1000,
        ReserveMet = false,
        ReservePriceCents = null
      };
      db.Auctions.Add(a);
      await db.SaveChangesAsync();
      auctionId = a.Id;
    }

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    // Read until we see a data line (or timeout)
    await using var stream = await res.Content.ReadAsStreamAsync();

    var sb = new StringBuilder();
    var buffer = new byte[1024];

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    while (!timeoutCts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
      if (n <= 0) break;

      sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
      var text = sb.ToString();

      // We expect an SSE event with data containing the JSON snapshot.
      if (text.Contains("data:", StringComparison.OrdinalIgnoreCase))
        break;
    }

    var all = sb.ToString();
    all.Should().Contain("event: snapshot");
    all.Should().Contain("data:");
    all.Should().Contain(auctionId.ToString());
  }
}