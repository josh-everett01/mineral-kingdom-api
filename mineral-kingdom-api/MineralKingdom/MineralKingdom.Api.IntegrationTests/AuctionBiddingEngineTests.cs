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

    var req = NewBidRequest(auctionId, userId, emailVerified: true, new { maxBidCents = 1050, mode = "IMMEDIATE" }); // $10.50 invalid
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
    // Start at $24.00 => increment should be $1, so minToBeat is $25.00
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 2400, reservePriceCents: null, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var leader = Guid.NewGuid();
    var challenger = Guid.NewGuid();

    // First bid by leader: $24 -> ok (minToBeat doesn't matter yet)
    var r1 = await client.SendAsync(NewBidRequest(auctionId, leader, true, new { maxBidCents = 2400, mode = "IMMEDIATE" }));
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    // Challenger tries $24 again (needs >= $25 to compete)
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

    // A max = $50
    var rA = await client.SendAsync(NewBidRequest(auctionId, a, true, new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    rA.StatusCode.Should().Be(HttpStatusCode.OK);

    // B max = $80
    var rB = await client.SendAsync(NewBidRequest(auctionId, b, true, new { maxBidCents = 8000, mode = "IMMEDIATE" }));
    rB.StatusCode.Should().Be(HttpStatusCode.OK);

    // Verify auction derived fields
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.CurrentLeaderUserId.Should().Be(b);
      auct.CurrentLeaderMaxCents.Should().Be(8000);

      // runner-up max is 5000. increment at $50 range (50–74 => $3) => 300 cents
      // price should be 5300 (clamped to winner max, but winner is higher)
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
  public async Task Delayed_bids_must_be_registered_3h_before_close_and_are_injected_at_closing_start()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    // Close in 4 hours => delayed still allowed (>=3h before close)
    var (auctionId, listingId) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: null, closeTime: now.AddHours(4));

    using var client = factory.CreateClient();
    var delayedUser = Guid.NewGuid();

    // Register delayed $50
    var reg = await client.SendAsync(NewBidRequest(auctionId, delayedUser, true, new { maxBidCents = 5000, mode = "DELAYED" }));
    reg.StatusCode.Should().Be(HttpStatusCode.OK);

    // While LIVE, delayed should not change current leader/price
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.Status.Should().Be(AuctionStatuses.Live);
      auct.CurrentLeaderUserId.Should().BeNull();
      auct.CurrentPriceCents.Should().Be(1000);
    }

    // Force closeTime in past and advance to CLOSING, which should inject delayed bids
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

    // After entering CLOSING, delayed bid should be applied (injected)
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      auct.Status.Should().Be(AuctionStatuses.Closing);
      auct.CurrentLeaderUserId.Should().Be(delayedUser);
      auct.CurrentLeaderMaxCents.Should().Be(5000);
      auct.CurrentPriceCents.Should().Be(1000 + BidIncrementTable.GetIncrementCents(1000));
      // single bidder => price can stay at starting price (runner-up = starting)
    }

    // Ensure delayed injection event logged (system)
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var has = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == auctionId && e.EventType == "DELAYED_BIDS_INJECTED");
      has.Should().BeTrue();
    }

    // Also ensure the delayed row is now IMMEDIATE
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var mb = await db.AuctionMaxBids.SingleAsync(x => x.AuctionId == auctionId && x.UserId == delayedUser);
      mb.BidType.Should().Be("IMMEDIATE");
    }
  }

  [Fact]
  public async Task Reserve_is_tracked_and_value_is_not_exposed_in_api_response_or_events()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    // Reserve = $60, starting $10
    var (auctionId, _) = await SeedLiveAuctionAsync(factory, now, startingPriceCents: 1000, reservePriceCents: 6000, closeTime: now.AddHours(6));

    using var client = factory.CreateClient();
    var user = Guid.NewGuid();

    // Place max $80 => reserve met, current price should lift to reserve (at least)
    var resp = await client.SendAsync(NewBidRequest(auctionId, user, true, new { maxBidCents = 8000, mode = "IMMEDIATE" }));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await resp.Content.ReadFromJsonAsync<PlaceBidResponseWire>();
    dto.Should().NotBeNull();
    dto!.ReserveMet.Should().BeTrue();
    dto.CurrentPriceCents.Should().Be(6000); // lifted to reserve
    dto.LeaderUserId.Should().Be(user);

    // Verify no event payload contains reserve value
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

      // Derived state should be deterministic: u2 should lead with 8000
      var auct = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auct.CurrentLeaderUserId.Should().Be(u2);

      // Every attempt logged (accepted or rejected)
      var attemptCount = await db.AuctionBidEvents.CountAsync(e =>
        e.AuctionId == auctionId &&
        (e.EventType == "BID_ACCEPTED" || e.EventType == "BID_REJECTED"));

      attemptCount.Should().Be(2);
    }
  }

  // ------------------------
  // Helpers
  // ------------------------

  private sealed record PlaceBidResponseWire(int CurrentPriceCents, Guid? LeaderUserId, bool ReserveMet);

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

  private static async Task AssertEventLoggedAsync(TestAppFactory factory, Guid auctionId, Guid userId, string eventType, bool accepted)
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