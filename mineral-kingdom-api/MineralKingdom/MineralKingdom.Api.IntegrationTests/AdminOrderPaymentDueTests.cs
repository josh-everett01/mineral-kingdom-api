using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;
using MineralKingdom.Contracts.Listings;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AdminOrderPaymentDueTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AdminOrderPaymentDueTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Owner_can_extend_payment_due_date_for_unpaid_auction_order_and_audit_is_written()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid auctionId;
    Guid orderId;
    Guid ownerUserId;

    // Seed sold closing auction and advance it to create unpaid order
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // winner
      var winnerUserId = Guid.NewGuid();
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

      // owner actor (not strictly required for auth headers, but good to have in DB)
      ownerUserId = Guid.NewGuid();
      db.Users.Add(new User
      {
        Id = ownerUserId,
        Email = $"owner-{ownerUserId:N}@example.com",
        PasswordHash = "x",
        EmailVerified = true,
        Role = UserRoles.Owner,
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

    DateTimeOffset originalDue;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var order = await db.Orders.SingleAsync(o => o.AuctionId == auctionId);
      orderId = order.Id;
      order.SourceType.Should().Be("AUCTION");
      order.Status.Should().Be("AWAITING_PAYMENT");
      order.PaymentDueAt.Should().NotBeNull();
      originalDue = order.PaymentDueAt!.Value;
    }

    // Act: extend payment due
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var newDue = originalDue.AddHours(24);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/payment-due", new ExtendPaymentDueRequest(newDue));
    res.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Assert: order updated + audit written
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var updated = await db.Orders.SingleAsync(o => o.Id == orderId);
      updated.PaymentDueAt.Should().Be(newDue);

      var audit = await db.AdminAuditLogs
        .AsNoTracking()
        .OrderByDescending(a => a.CreatedAt)
        .FirstOrDefaultAsync(a => a.EntityType == "ORDER" && a.EntityId == orderId && a.ActionType == "ORDER_PAYMENT_DUE_EXTENDED");

      audit.Should().NotBeNull();
      audit!.ActorUserId.Should().Be(ownerUserId);
      audit.ActorRole.Should().Be(UserRoles.Owner);
      audit.BeforeJson.Should().Contain("paymentDueAt");
      audit.AfterJson.Should().Contain("paymentDueAt");
    }
  }

  [Fact]
  public async Task Non_owner_cannot_extend_payment_due_date()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{Guid.NewGuid()}/payment-due",
      new ExtendPaymentDueRequest(DateTimeOffset.UtcNow.AddHours(72)));

    // AuthZ should fail at the controller boundary
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Owner_cannot_set_payment_due_in_the_past()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    // seed sold auction -> unpaid order + owner user
    var (orderId, _, ownerUserId) = await SeedUnpaidAuctionOrderAsync(factory, now);

    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/payment-due",
      new ExtendPaymentDueRequest(now.AddMinutes(-1)));

    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body!["error"].Should().Be("PAYMENT_DUE_MUST_BE_IN_FUTURE");
  }

  [Fact]
  public async Task Owner_cannot_shorten_payment_due_date()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    var (orderId, originalDue, ownerUserId) = await SeedUnpaidAuctionOrderAsync(factory, now);

    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/payment-due",
      new ExtendPaymentDueRequest(originalDue.AddMinutes(-1)));

    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body!["error"].Should().Be("PAYMENT_DUE_CANNOT_DECREASE");
  }

  [Fact]
  public async Task Owner_cannot_extend_payment_due_too_far_into_future()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    var (orderId, _, ownerUserId) = await SeedUnpaidAuctionOrderAsync(factory, now);

    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/payment-due",
      new ExtendPaymentDueRequest(now.AddDays(31)));

    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body!["error"].Should().Be("PAYMENT_DUE_TOO_FAR_IN_FUTURE");
  }

  private static async Task<(Guid OrderId, DateTimeOffset OriginalDue, Guid OwnerUserId)>
    SeedUnpaidAuctionOrderAsync(TestAppFactory factory, DateTimeOffset now)
  {
    Guid auctionId;
    Guid ownerUserId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var winnerUserId = Guid.NewGuid();
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

      ownerUserId = Guid.NewGuid();
      db.Users.Add(new User
      {
        Id = ownerUserId,
        Email = $"owner-{ownerUserId:N}@example.com",
        PasswordHash = "x",
        EmailVerified = true,
        Role = UserRoles.Owner,
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
      var order = await db.Orders.SingleAsync(o => o.AuctionId == auctionId);
      order.PaymentDueAt.Should().NotBeNull();
      return (order.Id, order.PaymentDueAt!.Value, ownerUserId);
    }
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}
