using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Dashboard;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class DashboardTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public DashboardTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsUser(HttpClient client, Guid userId, string role = UserRoles.User, bool emailVerified = true)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, emailVerified ? "true" : "false");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
  }

  [Fact]
  public async Task Dashboard_returns_empty_lists_when_user_has_no_data()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();

    // seed user only
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Users.Add(new User
      {
        Id = userId,
        Email = "dash_empty@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId);

    var res = await client.GetAsync("/api/me/dashboard");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<DashboardDto>();
    dto.Should().NotBeNull();

    dto!.WonAuctions.Should().BeEmpty();
    dto.UnpaidAuctionOrders.Should().BeEmpty();
    dto.PaidOrders.Should().BeEmpty();
    dto.OpenBox.Should().BeNull();
    dto.ShippingInvoices.Should().BeEmpty();
  }

  [Fact]
  public async Task Dashboard_returns_only_callers_data_even_when_other_users_have_records()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userA = Guid.NewGuid();
    var userB = Guid.NewGuid();

    Guid auctionA, auctionB;
    Guid orderA_unpaid, orderB_unpaid;
    Guid orderA_paid, orderB_paid;
    Guid openBoxA, openBoxB;
    Guid invA, invB;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.AddRange(
        new User { Id = userA, Email = "dashA@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        new User { Id = userB, Email = "dashB@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
      );

      // Auctions (won auctions are CLOSED_PAID and CurrentLeaderUserId == me)
      auctionA = Guid.NewGuid();
      auctionB = Guid.NewGuid();

      db.Auctions.AddRange(
        new Auction
        {
          Id = auctionA,
          ListingId = Guid.NewGuid(),
          Status = AuctionStatuses.ClosedPaid,
          CurrentLeaderUserId = userA,
          CurrentPriceCents = 1234,
          StartingPriceCents = 1000,
          CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-10), DateTimeKind.Utc),
          StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Utc),
          BidCount = 1,
          ReserveMet = false,
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddMinutes(-10)
        },
        new Auction
        {
          Id = auctionB,
          ListingId = Guid.NewGuid(),
          Status = AuctionStatuses.ClosedPaid,
          CurrentLeaderUserId = userB,
          CurrentPriceCents = 2222,
          StartingPriceCents = 1000,
          CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Utc),
          StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Utc),
          BidCount = 1,
          ReserveMet = false,
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddMinutes(-5)
        }
      );

      // Orders: unpaid auction + paid orders
      orderA_unpaid = Guid.NewGuid();
      orderB_unpaid = Guid.NewGuid();
      orderA_paid = Guid.NewGuid();
      orderB_paid = Guid.NewGuid();

      db.Orders.AddRange(
        new Order
        {
          Id = orderA_unpaid,
          UserId = userA,
          GuestEmail = null,
          OrderNumber = $"MK-AU-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(5),
          PaidAt = null,
          CurrencyCode = "USD",
          SubtotalCents = 1000,
          DiscountTotalCents = 0,
          TotalCents = 1000,
          CreatedAt = now.AddMinutes(-30),
          UpdatedAt = now.AddMinutes(-30)
        },
        new Order
        {
          Id = orderB_unpaid,
          UserId = userB,
          GuestEmail = null,
          OrderNumber = $"MK-BU-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(6),
          PaidAt = null,
          CurrencyCode = "USD",
          SubtotalCents = 2000,
          DiscountTotalCents = 0,
          TotalCents = 2000,
          CreatedAt = now.AddMinutes(-25),
          UpdatedAt = now.AddMinutes(-25)
        },
        new Order
        {
          Id = orderA_paid,
          UserId = userA,
          GuestEmail = null,
          OrderNumber = $"MK-PA-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now.AddMinutes(-60),
          CurrencyCode = "USD",
          SubtotalCents = 3000,
          DiscountTotalCents = 0,
          TotalCents = 3000,
          CreatedAt = now.AddMinutes(-70),
          UpdatedAt = now.AddMinutes(-60)
        },
        new Order
        {
          Id = orderB_paid,
          UserId = userB,
          GuestEmail = null,
          OrderNumber = $"MK-PB-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now.AddMinutes(-55),
          CurrencyCode = "USD",
          SubtotalCents = 4000,
          DiscountTotalCents = 0,
          TotalCents = 4000,
          CreatedAt = now.AddMinutes(-65),
          UpdatedAt = now.AddMinutes(-55)
        }
      );

      // Open boxes
      openBoxA = Guid.NewGuid();
      openBoxB = Guid.NewGuid();

      db.FulfillmentGroups.AddRange(
        new FulfillmentGroup
        {
          Id = openBoxA,
          UserId = userA,
          GuestEmail = null,
          BoxStatus = "OPEN",
          ClosedAt = null,
          Status = "READY_TO_FULFILL",
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddMinutes(-1)
        },
        new FulfillmentGroup
        {
          Id = openBoxB,
          UserId = userB,
          GuestEmail = null,
          BoxStatus = "OPEN",
          ClosedAt = null,
          Status = "READY_TO_FULFILL",
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddMinutes(-1)
        }
      );

      // Assign one paid order per open box
      var oa = await db.Orders.FindAsync(orderA_paid);
      oa!.FulfillmentGroupId = openBoxA;
      oa.UpdatedAt = now;

      var ob = await db.Orders.FindAsync(orderB_paid);
      ob!.FulfillmentGroupId = openBoxB;
      ob.UpdatedAt = now;

      // Shipping invoices (need closed groups to be "normal", but join should work either way)
      invA = Guid.NewGuid();
      invB = Guid.NewGuid();

      db.ShippingInvoices.AddRange(
        new ShippingInvoice
        {
          Id = invA,
          FulfillmentGroupId = openBoxA,
          AmountCents = 0,
          CurrencyCode = "USD",
          Status = "PAID",
          PaidAt = now,
          CreatedAt = now.AddMinutes(-10),
          UpdatedAt = now.AddMinutes(-10)
        },
        new ShippingInvoice
        {
          Id = invB,
          FulfillmentGroupId = openBoxB,
          AmountCents = 999,
          CurrencyCode = "USD",
          Status = "UNPAID",
          PaidAt = null,
          CreatedAt = now.AddMinutes(-9),
          UpdatedAt = now.AddMinutes(-9)
        }
      );

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userA);

    var res = await client.GetAsync("/api/me/dashboard");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<DashboardDto>();
    dto.Should().NotBeNull();

    dto!.WonAuctions.Select(x => x.AuctionId).Should().Contain(auctionA).And.NotContain(auctionB);
    dto.UnpaidAuctionOrders.Select(x => x.OrderId).Should().Contain(orderA_unpaid).And.NotContain(orderB_unpaid);
    dto.PaidOrders.Select(x => x.OrderId).Should().Contain(orderA_paid).And.NotContain(orderB_paid);

    dto.OpenBox.Should().NotBeNull();
    dto.OpenBox!.FulfillmentGroupId.Should().Be(openBoxA);
    dto.OpenBox.Orders.Select(o => o.OrderId).Should().Contain(orderA_paid);

    dto.ShippingInvoices.Select(i => i.ShippingInvoiceId).Should().Contain(invA).And.NotContain(invB);
  }

  [Fact]
  public async Task Dashboard_includes_unpaid_auction_orders_with_due_dates_sorted_by_due()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User { Id = userId, Email = "dash_due@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

      db.Orders.AddRange(
        new Order
        {
          Id = Guid.NewGuid(),
          UserId = userId,
          OrderNumber = $"MK-D1-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(10),
          CurrencyCode = "USD",
          SubtotalCents = 1000,
          DiscountTotalCents = 0,
          TotalCents = 1000,
          CreatedAt = now.AddMinutes(-10),
          UpdatedAt = now.AddMinutes(-10)
        },
        new Order
        {
          Id = Guid.NewGuid(),
          UserId = userId,
          OrderNumber = $"MK-D2-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(2),
          CurrencyCode = "USD",
          SubtotalCents = 2000,
          DiscountTotalCents = 0,
          TotalCents = 2000,
          CreatedAt = now.AddMinutes(-5),
          UpdatedAt = now.AddMinutes(-5)
        }
      );

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId);

    var dto = await client.GetFromJsonAsync<DashboardDto>("/api/me/dashboard");
    dto.Should().NotBeNull();

    dto!.UnpaidAuctionOrders.Should().HaveCount(2);

    dto.UnpaidAuctionOrders[0].PaymentDueAt.Should().NotBeNull();
    dto.UnpaidAuctionOrders[1].PaymentDueAt.Should().NotBeNull();

    dto.UnpaidAuctionOrders[0].PaymentDueAt!.Value
      .Should()
      .BeBefore(dto.UnpaidAuctionOrders[1].PaymentDueAt!.Value);
  }

  [Fact]
  public async Task Dashboard_open_box_null_when_no_open_box_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Users.Add(new User { Id = userId, Email = "dash_nobox@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

      // Create CLOSED group only
      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        BoxStatus = "CLOSED",
        ClosedAt = DateTimeOffset.UtcNow,
        Status = "READY_TO_FULFILL",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId);

    var dto = await client.GetFromJsonAsync<DashboardDto>("/api/me/dashboard");
    dto.Should().NotBeNull();
    dto!.OpenBox.Should().BeNull();
  }

  [Fact]
  public async Task Dashboard_does_not_include_guest_orders_without_userId()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User { Id = userId, Email = "dash_guest_excl@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

      // guest store order (no UserId)
      db.Orders.Add(new Order
      {
        Id = Guid.NewGuid(),
        UserId = null,
        GuestEmail = "guest@example.com",
        OrderNumber = $"MK-G-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 500,
        DiscountTotalCents = 0,
        TotalCents = 500,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId);

    var dto = await client.GetFromJsonAsync<DashboardDto>("/api/me/dashboard");
    dto.Should().NotBeNull();

    dto!.PaidOrders.Should().BeEmpty();
    dto.UnpaidAuctionOrders.Should().BeEmpty();
  }

  [Fact]
  public async Task Dashboard_limits_each_section_to_20_items()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User { Id = userId, Email = "dash_limit@example.com", EmailVerified = true, Role = UserRoles.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

      // 25 paid orders
      for (var i = 0; i < 25; i++)
      {
        db.Orders.Add(new Order
        {
          Id = Guid.NewGuid(),
          UserId = userId,
          GuestEmail = null,
          OrderNumber = $"MK-LIM-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now.AddMinutes(-i),
          CurrencyCode = "USD",
          SubtotalCents = 1000,
          DiscountTotalCents = 0,
          TotalCents = 1000,
          CreatedAt = now.AddMinutes(-i),
          UpdatedAt = now.AddMinutes(-i)
        });
      }

      // 25 unpaid auction orders
      for (var i = 0; i < 25; i++)
      {
        db.Orders.Add(new Order
        {
          Id = Guid.NewGuid(),
          UserId = userId,
          GuestEmail = null,
          OrderNumber = $"MK-LIMU-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(i),
          CurrencyCode = "USD",
          SubtotalCents = 1000,
          DiscountTotalCents = 0,
          TotalCents = 1000,
          CreatedAt = now.AddMinutes(-i),
          UpdatedAt = now.AddMinutes(-i)
        });
      }

      // 25 won auctions
      for (var i = 0; i < 25; i++)
      {
        db.Auctions.Add(new Auction
        {
          Id = Guid.NewGuid(),
          ListingId = Guid.NewGuid(),
          Status = AuctionStatuses.ClosedPaid,
          CurrentLeaderUserId = userId,
          CurrentPriceCents = 1000 + i,
          StartingPriceCents = 1000,
          CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-i), DateTimeKind.Utc),
          StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Utc),
          BidCount = 1,
          ReserveMet = false,
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddMinutes(-i)
        });
      }

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId);

    var dto = await client.GetFromJsonAsync<DashboardDto>("/api/me/dashboard");
    dto.Should().NotBeNull();

    dto!.PaidOrders.Count.Should().Be(20);
    dto.UnpaidAuctionOrders.Count.Should().Be(20);
    dto.WonAuctions.Count.Should().Be(20);
  }
}