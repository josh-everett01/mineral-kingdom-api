using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionSseBroadcastTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AuctionSseBroadcastTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Sse_receives_update_after_bid_commit()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var utc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    Guid auctionId;
    Guid userId;

    // Seed user + live auction
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var user = new User
      {
        Id = Guid.NewGuid(),
        Email = "sse_bid_user@example.com",
        EmailVerified = true,
        Role = UserRoles.Owner,
        CreatedAt = utc,
        UpdatedAt = utc
      };

      db.Users.Add(user);

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

      userId = user.Id;
      auctionId = a.Id;
    }

    // Connect SSE
    using var sseReq = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/events");
    var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead);
    sseRes.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var stream = await sseRes.Content.ReadAsStreamAsync();

    // Read initial event chunk
    var buf = new byte[4096];
    _ = await stream.ReadAsync(buf);

    // Place bid (Testing uses TestAuth as default scheme)
    var bidClient = factory.CreateClient();
    bidClient.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    bidClient.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    bidClient.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var bidRes = await bidClient.PostAsJsonAsync($"/api/auctions/{auctionId}/bids", new
    {
      maxBidCents = 1500,
      mode = "IMMEDIATE"
    });

    bidRes.StatusCode.Should().Be(HttpStatusCode.OK);

    // Read until we see bidCount=1 in a snapshot (or timeout)
    var sb = new StringBuilder();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    while (!cts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
      if (n <= 0) break;

      sb.Append(Encoding.UTF8.GetString(buf, 0, n));
      var text = sb.ToString();

      if (text.Contains("\"bidCount\":1", StringComparison.OrdinalIgnoreCase))
        break;
    }

    var all = sb.ToString();
    all.Should().Contain("event: snapshot");
    all.Should().Contain("data:");

    // IMPORTANT: take the last data line, not the first (first is often initial snapshot w/ bidCount=0)
    var dataLines = all.Split('\n')
      .Where(l => l.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
      .ToList();

    dataLines.Should().NotBeEmpty();

    var lastDataLine = dataLines.Last();
    var json = lastDataLine.Substring("data: ".Length);

    var snap = System.Text.Json.JsonSerializer.Deserialize<AuctionRealtimeSnapshot>(json);
    snap.Should().NotBeNull();
    snap!.AuctionId.Should().Be(auctionId);

    // This is now stable: we parsed the latest snapshot we received
    snap.BidCount.Should().Be(1);
    snap.CurrentPriceCents.Should().BeGreaterThan(0);
  }
}