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

public sealed class AuctionRelistRulesTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AuctionRelistRulesTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Relist_creates_new_live_auction_after_delay_when_reserve_not_met()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid oldAuctionId;
    Guid listingId;

    // Seed CLOSED_NOT_SOLD w/ reserve NOT met and old UpdatedAt
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Relist eligible",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);

      listingId = listing.Id;

      var old = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedNotSold,
        StartingPriceCents = 1000,
        ReservePriceCents = 2000,
        ReserveMet = false,
        StartTime = now.AddHours(-26),
        CloseTime = now.AddHours(-2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 1500,
        CurrentLeaderUserId = Guid.NewGuid(),
        CurrentLeaderMaxCents = 1800,
        BidCount = 3,
        RelistOfAuctionId = null,
        CreatedAt = now.AddHours(-26),
        UpdatedAt = now.AddMinutes(-11) // delay satisfied
      };

      db.Auctions.Add(old);
      await db.SaveChangesAsync();

      oldAuctionId = old.Id;
    }

    // Act: sweep advances due auctions (includes relist due)
    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var advanced = await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
      advanced.Should().BeGreaterThanOrEqualTo(1);

    }

    // Assert: exactly one relisted auction created + events logged
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var relisted = await db.Auctions.SingleOrDefaultAsync(a => a.RelistOfAuctionId == oldAuctionId);
      relisted.Should().NotBeNull();

      relisted!.ListingId.Should().Be(listingId);
      relisted.Status.Should().Be(AuctionStatuses.Live);

      relisted.StartTime.Should().Be(now);
      relisted.CloseTime.Should().BeAfter(now);

      relisted.CurrentPriceCents.Should().Be(relisted.StartingPriceCents);
      relisted.CurrentLeaderUserId.Should().BeNull();
      relisted.CurrentLeaderMaxCents.Should().BeNull();
      relisted.BidCount.Should().Be(0);
      relisted.ReserveMet.Should().BeFalse();

      var oldEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == oldAuctionId && e.EventType == "RELIST_TRIGGERED");
      oldEvent.Should().BeTrue();

      var newEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == relisted.Id && e.EventType == "AUCTION_RELISTED");
      newEvent.Should().BeTrue();
    }
  }

  [Fact]
  public async Task Relist_does_not_happen_when_reserve_is_null()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid oldAuctionId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "No reserve",
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
        ReservePriceCents = null, // <-- key
        ReserveMet = false,
        StartTime = now.AddHours(-26),
        CloseTime = now.AddHours(-2),
        CurrentPriceCents = 1000,
        BidCount = 0,
        CreatedAt = now.AddHours(-26),
        UpdatedAt = now.AddMinutes(-11)
      };

      db.Auctions.Add(old);
      await db.SaveChangesAsync();
      oldAuctionId = old.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var relisted = await db.Auctions.SingleOrDefaultAsync(a => a.RelistOfAuctionId == oldAuctionId);
      relisted.Should().BeNull();

      var oldEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == oldAuctionId && e.EventType == "RELIST_TRIGGERED");
      oldEvent.Should().BeFalse();
    }
  }

  [Fact]
  public async Task Relist_is_idempotent()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid oldAuctionId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Idempotent relist",
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
        ReservePriceCents = 2000,
        ReserveMet = false,
        StartTime = now.AddHours(-26),
        CloseTime = now.AddHours(-2),
        CurrentPriceCents = 1500,
        BidCount = 2,
        CreatedAt = now.AddHours(-26),
        UpdatedAt = now.AddMinutes(-11)
      };

      db.Auctions.Add(old);
      await db.SaveChangesAsync();
      oldAuctionId = old.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
      await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var count = await db.Auctions.CountAsync(a => a.RelistOfAuctionId == oldAuctionId);
      count.Should().Be(1);
    }
  }

  [Fact]
  public async Task Closing_finalizes_to_ClosedNotSold_then_relists_after_delay_when_reserve_not_met()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid listingId;
    Guid oldAuctionId;

    // Seed an auction that is due to finalize (CLOSING window already expired),
    // and will deterministically resolve to CLOSED_NOT_SOLD due to reserve NOT met.
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "E2E close then relist",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);
      listingId = listing.Id;

      var auction = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Status = AuctionStatuses.Closing,

        StartingPriceCents = 1000,
        ReservePriceCents = 2000,
        ReserveMet = false,

        StartTime = now.AddHours(-26),
        CloseTime = now.AddHours(-2),
        ClosingWindowEnd = now.AddMinutes(-1), // window expired => should finalize

        CurrentPriceCents = 1500,
        CurrentLeaderUserId = Guid.NewGuid(),
        CurrentLeaderMaxCents = 1800, // below reserve
        BidCount = 3,

        RelistOfAuctionId = null,

        CreatedAt = now.AddHours(-26),
        UpdatedAt = now.AddMinutes(-1) // will get overwritten on finalize anyway
      };

      db.Auctions.Add(auction);
      await db.SaveChangesAsync();

      oldAuctionId = auction.Id;
    }

    // Act 1: sweep should finalize CLOSING -> CLOSED_NOT_SOLD
    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var advanced = await sm.AdvanceDueAuctionsAsync(now, CancellationToken.None);
      advanced.Should().BeGreaterThan(0);
    }

    // Assert finalized state
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var old = await db.Auctions.SingleAsync(x => x.Id == oldAuctionId);

      old.Status.Should().Be(AuctionStatuses.ClosedNotSold);

      // sanity: still reserve-not-met shape
      old.ReservePriceCents.Should().NotBeNull();
      old.ReserveMet.Should().BeFalse();
    }

    // Arrange for relist eligibility:
    // relist sweep checks UpdatedAt <= now - RelistDelay
    // so we backdate UpdatedAt after finalize.
    var now2 = now.AddMinutes(11);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var old = await db.Auctions.SingleAsync(x => x.Id == oldAuctionId);

      old.UpdatedAt = now2.AddMinutes(-11); // ensure <= now2 - 10 minutes
      await db.SaveChangesAsync();
    }

    // Act 2: sweep should relist CLOSED_NOT_SOLD -> create new LIVE auction
    using (var scope = factory.Services.CreateScope())
    {
      var sm = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var advanced = await sm.AdvanceDueAuctionsAsync(now2, CancellationToken.None);
      advanced.Should().BeGreaterThan(0);
    }

    // Assert relisted auction + events
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var relisted = await db.Auctions.SingleOrDefaultAsync(a => a.RelistOfAuctionId == oldAuctionId);
      relisted.Should().NotBeNull();

      relisted!.ListingId.Should().Be(listingId);
      relisted.Status.Should().Be(AuctionStatuses.Live);

      relisted.StartTime.Should().Be(now2);
      relisted.CloseTime.Should().BeAfter(now2);

      relisted.CurrentPriceCents.Should().Be(relisted.StartingPriceCents);
      relisted.CurrentLeaderUserId.Should().BeNull();
      relisted.CurrentLeaderMaxCents.Should().BeNull();
      relisted.BidCount.Should().Be(0);
      relisted.ReserveMet.Should().BeFalse();

      var oldEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == oldAuctionId && e.EventType == "RELIST_TRIGGERED");
      oldEvent.Should().BeTrue();

      var newEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == relisted.Id && e.EventType == "AUCTION_RELISTED");
      newEvent.Should().BeTrue();
    }
  }
  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}
