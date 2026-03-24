using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionBiddingEngineTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionBiddingEngineTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Whole_dollars_only_is_enforced_and_rejected_attempt_is_logged()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var userId = Guid.NewGuid();

    var req = NewBidRequest(auctionId, userId, emailVerified: true, new { maxBidCents = 1050, mode = "IMMEDIATE" });
    var resp = await client.SendAsync(req);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    await AssertEventLoggedAsync(factory, auctionId, userId, "BID_REJECTED", accepted: false);
  }

  [Fact]
  public async Task Increment_table_is_enforced_when_competing()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 2400, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var leader = Guid.NewGuid();
    var challenger = Guid.NewGuid();

    var r1 = await client.SendAsync(NewBidRequest(auctionId, leader, true, new { maxBidCents = 2400, mode = "IMMEDIATE" }));
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    var r2 = await client.SendAsync(NewBidRequest(auctionId, challenger, true, new { maxBidCents = 2400, mode = "IMMEDIATE" }));
    r2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    await AssertEventLoggedAsync(factory, auctionId, challenger, "BID_REJECTED", accepted: false);
  }

  [Fact]
  public async Task Proxy_bidding_sets_current_price_to_runner_up_plus_increment_clamped_to_winner_max()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();

    var rA = await client.SendAsync(NewBidRequest(auctionId, a, true, new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    rA.StatusCode.Should().Be(HttpStatusCode.OK);

    var rB = await client.SendAsync(NewBidRequest(auctionId, b, true, new { maxBidCents = 8000, mode = "IMMEDIATE" }));
    rB.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.CurrentLeaderUserId.Should().Be(b);
      auct.CurrentLeaderMaxCents.Should().Be(8000);
      auct.CurrentPriceCents.Should().Be(5300);
    }

    await AssertEventLoggedAsync(factory, auctionId, a, "BID_ACCEPTED", accepted: true);
    await AssertEventLoggedAsync(factory, auctionId, b, "BID_ACCEPTED", accepted: true);
  }

  [Fact]
  public async Task Tie_breaker_earliest_received_wins_when_max_equal()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();

    var r1 = await client.SendAsync(NewBidRequest(auctionId, first, true, new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    var r2 = await client.SendAsync(NewBidRequest(auctionId, second, true, new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    r2.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.CurrentLeaderUserId.Should().Be(first);
      auct.CurrentLeaderMaxCents.Should().Be(5000);
    }
  }

  [Fact]
  public async Task Delayed_bids_must_be_registered_3h_before_close_and_are_activated_at_closing_start()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(4));

    using var client = factory.CreateClient();
    var delayedUser = Guid.NewGuid();

    var reg = await client.SendAsync(NewBidRequest(auctionId, delayedUser, true, new { maxBidCents = 5000, mode = "DELAYED" }));
    reg.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.Status.Should().Be(AuctionStatuses.Live);
      auct.CurrentLeaderUserId.Should().BeNull();
      auct.CurrentPriceCents.Should().Be(1000);

      var delayed = await db.AuctionDelayedBids.SingleAsync(x => x.AuctionId == auctionId && x.UserId == delayedUser);
      delayed.MaxBidCents.Should().Be(5000);
      delayed.Status.Should().Be("SCHEDULED");
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auct.CloseTime = now.AddMinutes(-1);
      auct.UpdatedAt = now;
      await db.SaveChangesAsync();
    }

    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await sm.AdvanceAuctionAsync(auctionId, now, CancellationToken.None);
      changed.Should().BeTrue(err);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.Status.Should().Be(AuctionStatuses.Closing);
      auct.CurrentLeaderUserId.Should().Be(delayedUser);
      auct.CurrentLeaderMaxCents.Should().Be(5000);
      auct.CurrentPriceCents.Should().Be(1000 + BidIncrementTable.GetIncrementCents(1000));

      var delayed = await db.AuctionDelayedBids.SingleAsync(x => x.AuctionId == auctionId && x.UserId == delayedUser);
      delayed.Status.Should().Be("ACTIVATED");
      delayed.ActivatedAt.Should().NotBeNull();
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var has = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == auctionId && e.EventType == "DELAYED_BIDS_INJECTED");
      has.Should().BeTrue();
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var mb = await db.AuctionMaxBids.SingleAsync(x => x.AuctionId == auctionId && x.UserId == delayedUser);
      mb.BidType.Should().Be("IMMEDIATE");
      mb.MaxBidCents.Should().Be(5000);
    }
  }

  [Fact]
  public async Task New_delayed_bid_replaces_existing_delayed_bid_for_same_user_and_auction()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(4));

    using var client = factory.CreateClient();
    var userId = Guid.NewGuid();

    var first = await client.SendAsync(NewBidRequest(auctionId, userId, true, new { maxBidCents = 5000, mode = "DELAYED" }));
    first.StatusCode.Should().Be(HttpStatusCode.OK);

    var second = await client.SendAsync(NewBidRequest(auctionId, userId, true, new { maxBidCents = 7000, mode = "DELAYED" }));
    second.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var delayedBids = await db.AuctionDelayedBids
        .Where(x => x.AuctionId == auctionId && x.UserId == userId)
        .ToListAsync();

      delayedBids.Should().HaveCount(1);
      delayedBids[0].MaxBidCents.Should().Be(7000);
      delayedBids[0].Status.Should().Be("SCHEDULED");
    }
  }

  [Fact]
  public async Task Cancel_delayed_bid_marks_it_cancelled_and_returns_no_content()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(4));

    using var client = factory.CreateClient();
    var userId = Guid.NewGuid();

    var reg = await client.SendAsync(NewBidRequest(auctionId, userId, true, new { maxBidCents = 5000, mode = "DELAYED" }));
    reg.StatusCode.Should().Be(HttpStatusCode.OK);

    var cancel = await client.SendAsync(NewCancelDelayedBidRequest(auctionId, userId, true));
    cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var delayed = await db.AuctionDelayedBids.SingleAsync(x => x.AuctionId == auctionId && x.UserId == userId);
      delayed.Status.Should().Be("CANCELLED");
      delayed.CancelledAt.Should().NotBeNull();
    }

    await AssertEventLoggedAsync(factory, auctionId, userId, "DELAYED_BID_CANCELLED", accepted: true);
  }

  [Fact]
  public async Task Cancel_delayed_bid_rejects_when_no_delayed_bid_exists()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(4));

    using var client = factory.CreateClient();
    var userId = Guid.NewGuid();

    var cancel = await client.SendAsync(NewCancelDelayedBidRequest(auctionId, userId, true));
    cancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Reserve_is_tracked_and_value_is_not_exposed_in_api_response_or_events()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: 6000, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var user = Guid.NewGuid();

    var resp = await client.SendAsync(NewBidRequest(auctionId, user, true, new { maxBidCents = 8000, mode = "IMMEDIATE" }));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await resp.Content.ReadFromJsonAsync<PlaceBidResponseWire>();
    dto.Should().NotBeNull();
    dto!.ReserveMet.Should().BeTrue();
    dto.CurrentPriceCents.Should().Be(6000);
    dto.LeaderUserId.Should().Be(user);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var events = await db.AuctionBidEvents
        .Where(e => e.AuctionId == auctionId)
        .ToListAsync();

      events.Should().NotBeEmpty();
      events.Any(e => (e.DataJson ?? "").Contains("6000", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
      events.Any(e => (e.DataJson ?? "").Contains("reserve", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }
  }

  [Fact]
  public async Task Bid_placement_is_concurrency_safe_and_all_attempts_are_logged()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var u1 = Guid.NewGuid();
    var u2 = Guid.NewGuid();

    var t1 = client.SendAsync(NewBidRequest(auctionId, u1, true, new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    var t2 = client.SendAsync(NewBidRequest(auctionId, u2, true, new { maxBidCents = 8000, mode = "IMMEDIATE" }));

    await Task.WhenAll(t1, t2);

    (t1.Result.StatusCode == HttpStatusCode.OK || t1.Result.StatusCode == HttpStatusCode.BadRequest).Should().BeTrue();
    (t2.Result.StatusCode == HttpStatusCode.OK || t2.Result.StatusCode == HttpStatusCode.BadRequest).Should().BeTrue();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auct.CurrentLeaderUserId.Should().Be(u2);

      var attemptCount = await db.AuctionBidEvents.CountAsync(e =>
        e.AuctionId == auctionId &&
        (e.EventType == "BID_ACCEPTED" || e.EventType == "BID_REJECTED"));

      attemptCount.Should().Be(2);
    }
  }

  private sealed record PlaceBidResponseWire(
    int CurrentPriceCents,
    Guid? LeaderUserId,
    bool HasReserve,
    bool? ReserveMet
  );

  private TestAppFactory NewFactory()
    => new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static HttpRequestMessage NewBidRequest(Guid auctionId, Guid userId, bool emailVerified, object body)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/auctions/{auctionId}/bids");
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", emailVerified ? "true" : "false");
    req.Content = JsonContent.Create(body);
    return req;
  }

  private static HttpRequestMessage NewCancelDelayedBidRequest(Guid auctionId, Guid userId, bool emailVerified)
  {
    var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/auctions/{auctionId}/delayed-bid");
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", emailVerified ? "true" : "false");
    return req;
  }

  private static async Task<(Guid AuctionId, Guid ListingId)> SeedLiveAuctionAsync(
    TestAppFactory factory,
    DateTimeOffset now,
    int startingPriceCents,
    int? reservePriceCents,
    DateTimeOffset closeTime)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Auction Listing",
      Description = "Test",
      Status = ListingStatuses.Published,
      QuantityAvailable = 1,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);

    var auction = new Auction
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      Status = AuctionStatuses.Live,
      StartingPriceCents = startingPriceCents,
      ReservePriceCents = reservePriceCents,
      StartTime = now.AddMinutes(-5),
      CloseTime = closeTime,
      ClosingWindowEnd = null,
      CurrentPriceCents = startingPriceCents,
      CurrentLeaderUserId = null,
      CurrentLeaderMaxCents = null,
      BidCount = 0,
      ReserveMet = false,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Auctions.Add(auction);
    await db.SaveChangesAsync();

    return (auction.Id, listing.Id);
  }

  private static async Task AssertEventLoggedAsync(
    TestAppFactory factory,
    Guid auctionId,
    Guid userId,
    string eventType,
    bool accepted)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var has = await db.AuctionBidEvents.AnyAsync(e =>
      e.AuctionId == auctionId &&
      e.UserId == userId &&
      e.EventType == eventType &&
      e.Accepted == accepted);

    has.Should().BeTrue($"Expected event {eventType} for user {userId} to be recorded");
  }
}