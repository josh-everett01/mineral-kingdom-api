using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OptionalOutbidEmailTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public OptionalOutbidEmailTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Outbid_email_is_sent_by_default_and_deduped()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid u1;
    Guid u2;
    Guid auctionId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      u1 = Guid.NewGuid();
      u2 = Guid.NewGuid();

      db.Users.AddRange(
        new User { Id = u1, Email = "outbid1@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        new User { Id = u2, Email = "outbid2@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
      );

      var now = DateTimeOffset.UtcNow;

      var a = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = Guid.NewGuid(),
        Status = AuctionStatuses.Live,
        CreatedAt = now,
        UpdatedAt = now,
        CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Utc),
        StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Utc),
        StartingPriceCents = 1000,
        CurrentPriceCents = 1000,
        BidCount = 0,
        ReserveMet = false,
        ReservePriceCents = null
      };

      db.Auctions.Add(a);
      await db.SaveChangesAsync();
      auctionId = a.Id;
    }

    // Bid 1: u1 becomes leader (no outbid email)
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var bidding = scope2.ServiceProvider.GetRequiredService<AuctionBiddingService>();
      var now = DateTimeOffset.UtcNow;

      var r1 = await bidding.PlaceBidAsync(auctionId, u1, maxBidCents: 1500, mode: "IMMEDIATE", now: now, ct: CancellationToken.None);
      r1.Ok.Should().BeTrue();
    }

    // Bid 2: u2 outbids u1 => should enqueue OUTBID email to u1
    await using (var scope3 = factory.Services.CreateAsyncScope())
    {
      var bidding = scope3.ServiceProvider.GetRequiredService<AuctionBiddingService>();
      var now = DateTimeOffset.UtcNow;

      var r2 = await bidding.PlaceBidAsync(auctionId, u2, maxBidCents: 2000, mode: "IMMEDIATE", now: now, ct: CancellationToken.None);
      r2.Ok.Should().BeTrue();
    }

    await using (var scope4 = factory.Services.CreateAsyncScope())
    {
      var db = scope4.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var rows = await db.EmailOutbox.AsNoTracking()
        .Where(x => x.TemplateKey == "OUTBID" && x.ToEmail == "outbid1@example.com")
        .ToListAsync();

      rows.Should().HaveCount(1);
    }
  }

  [Fact]
  public async Task Outbid_email_respects_user_preference_toggle()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid u1;
    Guid u2;
    Guid auctionId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      u1 = Guid.NewGuid();
      u2 = Guid.NewGuid();

      db.Users.AddRange(
        new User { Id = u1, Email = "outbid_off_1@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        new User { Id = u2, Email = "outbid_off_2@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
      );

      // Disable outbid emails for u1
      db.UserNotificationPreferences.Add(new UserNotificationPreferences
      {
        UserId = u1,
        OutbidEmailEnabled = false,
        AuctionPaymentRemindersEnabled = true,
        ShippingInvoiceRemindersEnabled = true,
        BidAcceptedEmailEnabled = false,
        UpdatedAt = DateTimeOffset.UtcNow
      });

      var now = DateTimeOffset.UtcNow;

      var a = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = Guid.NewGuid(),
        Status = AuctionStatuses.Live,
        CreatedAt = now,
        UpdatedAt = now,
        CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Utc),
        StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Utc),
        StartingPriceCents = 1000,
        CurrentPriceCents = 1000,
        BidCount = 0,
        ReserveMet = false,
        ReservePriceCents = null
      };

      db.Auctions.Add(a);
      await db.SaveChangesAsync();
      auctionId = a.Id;
    }

    // u1 first bid (leader)
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var bidding = scope2.ServiceProvider.GetRequiredService<AuctionBiddingService>();
      var now = DateTimeOffset.UtcNow;

      var r1 = await bidding.PlaceBidAsync(auctionId, u1, 1500, "IMMEDIATE", now, CancellationToken.None);
      r1.Ok.Should().BeTrue();
    }

    // u2 outbids u1, but u1 has outbid emails disabled => no outbox row
    await using (var scope3 = factory.Services.CreateAsyncScope())
    {
      var bidding = scope3.ServiceProvider.GetRequiredService<AuctionBiddingService>();
      var now = DateTimeOffset.UtcNow;

      var r2 = await bidding.PlaceBidAsync(auctionId, u2, 2000, "IMMEDIATE", now, CancellationToken.None);
      r2.Ok.Should().BeTrue();
    }

    await using (var scope4 = factory.Services.CreateAsyncScope())
    {
      var db = scope4.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var rows = await db.EmailOutbox.AsNoTracking()
        .Where(x => x.TemplateKey == "OUTBID" && x.ToEmail == "outbid_off_1@example.com")
        .ToListAsync();

      rows.Should().BeEmpty();
    }
  }
}