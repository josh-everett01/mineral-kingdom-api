using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrdersListTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public OrdersListTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Winner_can_list_orders_and_see_unpaid_auction_order_and_due_date()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    Guid auctionId;
    Guid winnerUserId;

    // Seed auction that will finalize to ClosedWaitingOnPayment
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

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
        CurrentLeaderUserId = winnerUserId,
        CurrentLeaderMaxCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Auctions.Add(auction);
      await db.SaveChangesAsync();
      auctionId = auction.Id;
    }

    // Advance to sold close (creates unpaid order)
    using (var scope = factory.Services.CreateScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<AuctionStateMachineService>();
      var (changed, err) = await svc.AdvanceAuctionAsync(auctionId, now, CancellationToken.None);
      changed.Should().BeTrue(err);
    }

    // Call GET /api/orders as winner
    var client = factory.CreateClient();

    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, winnerUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);


    var res = await client.GetAsync("/api/orders");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var orders = await res.Content.ReadFromJsonAsync<List<OrderDto>>();
    orders.Should().NotBeNull();
    orders!.Should().ContainSingle(o => o.SourceType == "AUCTION" && o.AuctionId == auctionId);

    var auctionOrder = orders.Single(o => o.AuctionId == auctionId);
    auctionOrder.Status.Should().Be("AWAITING_PAYMENT");
    auctionOrder.PaymentDueAt.Should().NotBeNull();
    orders!.All(o => o.UserId == winnerUserId).Should().BeTrue();
    auctionOrder.SourceType.Should().Be("AUCTION");
    auctionOrder.PaymentDueAt!.Value.Should().BeAfter(now.AddHours(47));
    auctionOrder.PaymentDueAt!.Value.Should().BeBefore(now.AddHours(49));
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}
