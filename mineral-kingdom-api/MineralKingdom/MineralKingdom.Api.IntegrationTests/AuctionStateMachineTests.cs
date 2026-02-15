using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionStateMachineTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionStateMachineTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Live_transitions_to_Closing_when_close_time_passes_and_sets_closing_window_end()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid auctionId;

    using (var scope = factory.Services.CreateScope())
    {
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
        StartingPriceCents = 1000,
        ReservePriceCents = null,
        StartTime = now.AddMinutes(-5),
        CloseTime = now.AddMinutes(-1),
        ClosingWindowEnd = null,
        CurrentPriceCents = 1000,
        BidCount = 0,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Auctions.Add(auction);

      await db.SaveChangesAsync();

      auctionId = auction.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await svc.AdvanceAuctionAsync(auctionId, now, CancellationToken.None);
      changed.Should().BeTrue(err);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      a.Status.Should().Be(AuctionStatuses.Closing);
      a.ClosingWindowEnd.Should().NotBeNull();
      a.ClosingWindowEnd!.Value.Should().BeAfter(now);

      var hasEvent = await db.AuctionBidEvents.AnyAsync(e =>
        e.AuctionId == auctionId && e.EventType == "STATUS_CHANGED");

      hasEvent.Should().BeTrue();
    }
  }

  [Fact]
  public async Task Closing_transitions_to_ClosedNotSold_when_window_end_passes_and_not_reserve_met()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid auctionId;

    using (var scope = factory.Services.CreateScope())
    {
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
        Status = AuctionStatuses.Closing,
        StartingPriceCents = 1000,
        CloseTime = now.AddMinutes(-20),
        ClosingWindowEnd = now.AddMinutes(-1),
        CurrentPriceCents = 1000,
        BidCount = 0,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Auctions.Add(auction);
      await db.SaveChangesAsync();
      auctionId = auction.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await svc.AdvanceAuctionAsync(auctionId, now, CancellationToken.None);
      changed.Should().BeTrue(err);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      a.Status.Should().Be(AuctionStatuses.ClosedNotSold);
    }
  }

  [Fact]
  public async Task Closing_transitions_to_ClosedWaitingOnPayment_when_window_end_passes_and_reserve_met_and_has_bids()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid auctionId;
    Guid winnerUserId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Seed a winner user (sold auction must have a leader now that we create an unpaid order)
      winnerUserId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = winnerUserId,
        Email = $"winner-{winnerUserId:N}@example.com",
        PasswordHash = "x",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

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
        Status = AuctionStatuses.Closing,
        StartingPriceCents = 1000,
        ReservePriceCents = 1200,
        CloseTime = now.AddMinutes(-20),
        ClosingWindowEnd = now.AddMinutes(-1),
        CurrentPriceCents = 1300,
        BidCount = 3,
        ReserveMet = true,

        // NEW: winner fields required for sold close → unpaid order
        CurrentLeaderUserId = winnerUserId,
        CurrentLeaderMaxCents = 1300,

        CreatedAt = now,
        UpdatedAt = now
      };

      db.Auctions.Add(auction);
      await db.SaveChangesAsync();
      auctionId = auction.Id;
    }

    using (var scope = factory.Services.CreateScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await svc.AdvanceAuctionAsync(auctionId, now, CancellationToken.None);
      changed.Should().BeTrue(err);
    }

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var a = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      a.Status.Should().Be(AuctionStatuses.ClosedWaitingOnPayment);

      // Optional: assert the unpaid auction order was created (core S5-4 behavior)
      var order = await db.Orders
        .Include(o => o.Lines)
        .SingleOrDefaultAsync(o => o.AuctionId == auctionId);

      order.Should().NotBeNull();
      order!.SourceType.Should().Be("AUCTION");
      order.Status.Should().Be("AWAITING_PAYMENT");
      order.UserId.Should().Be(winnerUserId);
      order.PaymentDueAt.Should().NotBeNull();
      order.PaymentDueAt!.Value.Should().BeAfter(now.AddHours(47)); // loose check
      order.Lines.Should().HaveCount(1);
      order.Lines[0].OfferId.Should().BeNull();
      order.Lines[0].ListingId.Should().Be(a.ListingId);
    }
  }


  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}
