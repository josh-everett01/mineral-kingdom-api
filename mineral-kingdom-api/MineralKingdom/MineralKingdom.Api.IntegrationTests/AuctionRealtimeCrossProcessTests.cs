using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionRealtimeCrossProcessTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionRealtimeCrossProcessTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Postgres_notification_is_fanned_out_to_api_sse_subscribers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var auctionId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = Guid.NewGuid(),
        Status = AuctionStatuses.Live,
        CreatedAt = now,
        UpdatedAt = now,
        BidCount = 0,
        StartTime = DateTime.UtcNow.AddMinutes(-5),
        CloseTime = DateTime.UtcNow.AddMinutes(30),
        CurrentPriceCents = 1000,
        StartingPriceCents = 1000,
        ReserveMet = false,
        ReservePriceCents = null
      });

      await db.SaveChangesAsync();
    }

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream, Encoding.UTF8);

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

    var initial = await ReadNextEventAsync(reader, timeoutCts.Token);
    initial.Should().Contain("event: snapshot");
    initial.Should().Contain($"\"Status\":\"{AuctionStatuses.Live}\"");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var notifier = scope.ServiceProvider.GetRequiredService<IAuctionRealtimeNotifier>();

      var auction = await db.Auctions.FindAsync(new object[] { auctionId }, timeoutCts.Token);
      auction.Should().NotBeNull();

      auction!.Status = AuctionStatuses.Closing;
      auction.ClosingWindowEnd = now.AddMinutes(10).UtcDateTime;
      auction.UpdatedAt = now;

      await db.SaveChangesAsync(timeoutCts.Token);
      await notifier.NotifyAuctionChangedAsync(auctionId, timeoutCts.Token);
    }

    var updated = await ReadNextEventAsync(reader, timeoutCts.Token);
    updated.Should().Contain("event: snapshot");
    updated.Should().Contain($"\"AuctionId\":\"{auctionId}\"");
    updated.Should().Contain($"\"Status\":\"{AuctionStatuses.Closing}\"");
  }

  [Fact]
  public async Task State_machine_transition_is_fanned_out_to_api_sse_subscribers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var auctionId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = Guid.NewGuid(),
        Status = AuctionStatuses.Live,
        CreatedAt = now,
        UpdatedAt = now,
        BidCount = 0,
        StartTime = DateTime.UtcNow.AddMinutes(-5),
        CloseTime = DateTime.UtcNow.AddSeconds(-1),
        CurrentPriceCents = 1000,
        StartingPriceCents = 1000,
        ReserveMet = false,
        ReservePriceCents = null
      });

      await db.SaveChangesAsync();
    }

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream, Encoding.UTF8);

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

    var initial = await ReadNextEventAsync(reader, timeoutCts.Token);
    initial.Should().Contain("event: snapshot");
    initial.Should().Contain($"\"Status\":\"{AuctionStatuses.Live}\"");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await sm.AdvanceAuctionAsync(auctionId, now, timeoutCts.Token);

      changed.Should().BeTrue();
      err.Should().BeNull();
    }

    var updated = await ReadNextEventAsync(reader, timeoutCts.Token);
    updated.Should().Contain("event: snapshot");
    updated.Should().Contain($"\"AuctionId\":\"{auctionId}\"");
    updated.Should().Contain($"\"Status\":\"{AuctionStatuses.Closing}\"");
  }

  private static async Task<string> ReadNextEventAsync(StreamReader reader, CancellationToken ct)
  {
    var sb = new StringBuilder();

    while (!ct.IsCancellationRequested)
    {
      var line = await reader.ReadLineAsync(ct);

      if (line is null)
        continue;

      if (line.Length == 0)
      {
        if (sb.Length == 0)
          continue;

        var block = sb.ToString();
        sb.Clear();

        if (block.StartsWith(":", StringComparison.Ordinal))
          continue;

        return block;
      }

      sb.AppendLine(line);
    }

    throw new TimeoutException("Timed out waiting for SSE event.");
  }
}