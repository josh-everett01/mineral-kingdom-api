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

public sealed class AuctionClosingLoopTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionClosingLoopTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Bidding_after_CloseTime_forces_CLOSING_and_sets_ClosingWindowEnd()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    var now = DateTimeOffset.UtcNow;
    var (auctionId, userId) = await SeedLiveAuctionCloseInPastAsync(factory, now);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/auctions/{auctionId}/bids");
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Content = JsonContent.Create(new { maxBidCents = 5000, mode = "IMMEDIATE" });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      a.Status.Should().Be(AuctionStatuses.Closing);
      a.ClosingWindowEnd.Should().NotBeNull();
      a.ClosingWindowEnd!.Value.Should().BeAfter(now.AddMinutes(9)); // approx now+10m (avoid flakiness)
    }
  }

  private static async Task<(Guid AuctionId, Guid UserId)> SeedLiveAuctionCloseInPastAsync(TestAppFactory factory, DateTimeOffset now)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "CloseTime Past",
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
      StartingPriceCents = 1000,
      CurrentPriceCents = 1000,
      BidCount = 0,
      ReserveMet = false,
      StartTime = now.AddHours(-2),
      CloseTime = now.AddMinutes(-1),
      ClosingWindowEnd = null,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Auctions.Add(auction);

    await db.SaveChangesAsync();

    return (auction.Id, Guid.NewGuid());
  }

  [Fact]
  public async Task ClosedNotSold_relists_after_10_minutes_and_links_to_prior_auction()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    var now = DateTimeOffset.UtcNow;

    Guid oldAuctionId;
    Guid listingId;

    // Seed CLOSED_NOT_SOLD older than 10 minutes
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Relist me",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);

      var old = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        RelistOfAuctionId = null,

        Status = AuctionStatuses.ClosedNotSold,

        StartingPriceCents = 1000,
        ReservePriceCents = 2000, // reserve not met scenario
        StartTime = now.AddHours(-26),
        CloseTime = now.AddHours(-2),
        ClosingWindowEnd = null,

        CurrentPriceCents = 1500,
        CurrentLeaderUserId = null,
        CurrentLeaderMaxCents = null,
        BidCount = 0,
        ReserveMet = false,

        CreatedAt = now.AddHours(-26),
        UpdatedAt = now.AddMinutes(-11) // older than 10 min
      };

      db.Auctions.Add(old);
      await db.SaveChangesAsync();

      oldAuctionId = old.Id;
      listingId = listing.Id;
    }

    // Run sweep
    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var advanced = await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
      advanced.Should().BeGreaterThan(0);
    }

    // Assert relist created exactly once, linked, and reset fields
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var relisted = await db.Auctions
        .SingleOrDefaultAsync(a => a.RelistOfAuctionId == oldAuctionId);

      relisted.Should().NotBeNull();

      relisted!.ListingId.Should().Be(listingId);
      relisted.Status.Should().Be(AuctionStatuses.Live);

      relisted.StartingPriceCents.Should().Be(1000);
      relisted.ReservePriceCents.Should().Be(2000);

      relisted.CurrentLeaderUserId.Should().BeNull();
      relisted.CurrentLeaderMaxCents.Should().BeNull();
      relisted.BidCount.Should().Be(0);
      relisted.ReserveMet.Should().BeFalse();
      relisted.CurrentPriceCents.Should().Be(relisted.StartingPriceCents);

      // Basic sanity: close is in the future
      relisted.StartTime.Should().NotBeNull();
      relisted.CloseTime.Should().BeAfter(now);
    }

    // Assert events logged (system)
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      (await db.AuctionBidEvents.AnyAsync(e => e.AuctionId == oldAuctionId && e.EventType == "RELIST_TRIGGERED"))
        .Should().BeTrue();

      var newAuctionId = await db.Auctions
        .Where(a => a.RelistOfAuctionId == oldAuctionId)
        .Select(a => a.Id)
        .SingleAsync();

      (await db.AuctionBidEvents.AnyAsync(e => e.AuctionId == newAuctionId && e.EventType == "AUCTION_RELISTED"))
        .Should().BeTrue();
    }
  }

  [Fact]
  public async Task Relist_is_idempotent_and_does_not_create_multiple_new_auctions()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    var now = DateTimeOffset.UtcNow;

    Guid oldAuctionId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Relist idempotency",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);

      var old = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedNotSold,
        StartingPriceCents = 1000,
        ReservePriceCents = 1500,      // ✅ must be non-null for relist rule
        StartTime = now.AddHours(-5),
        CloseTime = now.AddHours(-1),
        CurrentPriceCents = 1000,
        BidCount = 1,                  // optional, but reasonable
        ReserveMet = false,            // ✅ not met
        CreatedAt = now.AddHours(-5),
        UpdatedAt = now.AddMinutes(-20) // eligible
      };

      db.Auctions.Add(old);
      await db.SaveChangesAsync();

      oldAuctionId = old.Id;
    }

    // Run sweep twice
    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
      await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
    }

    // Only one relist should exist
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var count = await db.Auctions.CountAsync(a => a.RelistOfAuctionId == oldAuctionId);
      count.Should().Be(1);
    }
  }

  [Fact]
  public async Task Accepted_bid_during_CLOSING_extends_ClosingWindowEnd()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    var now = DateTimeOffset.UtcNow;

    // Seed auction already in CLOSING with a short window remaining
    Guid auctionId;
    var user1 = Guid.NewGuid();
    var user2 = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Closing extend",
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
        Status = AuctionStatuses.Closing,
        StartingPriceCents = 1000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddMinutes(-1),
        ClosingWindowEnd = now.AddMinutes(1), // about to expire

        CurrentPriceCents = 1000,
        CurrentLeaderUserId = null,
        CurrentLeaderMaxCents = null,
        BidCount = 0,
        ReserveMet = false,

        CreatedAt = now,
        UpdatedAt = now
      };

      db.Auctions.Add(auction);
      await db.SaveChangesAsync();
      auctionId = auction.Id;
    }

    using var client = factory.CreateClient();

    // First bid to establish cw1 (should extend to now+10)
    var r1 = await client.SendAsync(NewBidRequest(auctionId, user1, true,
      new { maxBidCents = 5000, mode = "IMMEDIATE" }));
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    DateTimeOffset cw1;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      a.Status.Should().Be(AuctionStatuses.Closing);
      a.ClosingWindowEnd.Should().NotBeNull();
      cw1 = a.ClosingWindowEnd!.Value;
    }

    // Second bid a moment later should push it further (cw2 > cw1)
    var r2 = await client.SendAsync(NewBidRequest(auctionId, user2, true,
      new { maxBidCents = 6000, mode = "IMMEDIATE" }));
    r2.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);

      a.ClosingWindowEnd.Should().NotBeNull();
      a.ClosingWindowEnd!.Value.Should().BeAfter(cw1);
    }
  }

  private static HttpRequestMessage NewBidRequest(Guid auctionId, Guid userId, bool emailVerified, object body)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/auctions/{auctionId}/bids");
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", emailVerified ? "true" : "false");
    req.Content = JsonContent.Create(body);
    return req;
  }
}
