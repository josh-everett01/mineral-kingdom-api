using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionShippingChoiceTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionShippingChoiceTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Ship_now_choice_copies_quoted_shipping_and_recomputes_total()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    Guid userId;
    Guid orderId;
    Guid auctionId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      userId = Guid.NewGuid();
      auctionId = Guid.NewGuid();
      orderId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "shipnow@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = Guid.NewGuid(),
        Status = "CLOSED_WAITING_ON_PAYMENT",
        StartingPriceCents = 1000,
        CurrentPriceCents = 1300,
        BidCount = 2,
        ReserveMet = true,
        QuotedShippingCents = 250,
        CloseTime = now.AddMinutes(-20),
        ClosingWindowEnd = now.AddMinutes(-10),
        CurrentLeaderUserId = userId,
        CurrentLeaderMaxCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        OrderNumber = "MK-SHIP-CHOICE-001",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(12),
        ShippingMode = AuctionShippingModes.Unselected,
        ShippingAmountCents = 0,
        CurrencyCode = "USD",
        SubtotalCents = 1300,
        DiscountTotalCents = 0,
        TotalCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.PostAsJsonAsync(
      $"/api/orders/{orderId}/auction-shipping-choice",
      new SetAuctionShippingChoiceRequest(AuctionShippingModes.ShipNow));

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<AuctionShippingChoiceResponse>();
    dto.Should().NotBeNull();
    dto!.OrderId.Should().Be(orderId);
    dto.ShippingMode.Should().Be(AuctionShippingModes.ShipNow);
    dto.SubtotalCents.Should().Be(1300);
    dto.ShippingAmountCents.Should().Be(250);
    dto.TotalCents.Should().Be(1550);

    await using var verifyScope = factory.Services.CreateAsyncScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var order = await verifyDb.Orders.SingleAsync(o => o.Id == orderId);
    order.ShippingMode.Should().Be(AuctionShippingModes.ShipNow);
    order.ShippingAmountCents.Should().Be(250);
    order.TotalCents.Should().Be(1550);
  }

  [Fact]
  public async Task Open_box_choice_keeps_shipping_zero_and_total_item_only()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    Guid userId;
    Guid orderId;
    Guid auctionId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      userId = Guid.NewGuid();
      auctionId = Guid.NewGuid();
      orderId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "openbox-choice@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = Guid.NewGuid(),
        Status = "CLOSED_WAITING_ON_PAYMENT",
        StartingPriceCents = 1000,
        CurrentPriceCents = 1300,
        BidCount = 2,
        ReserveMet = true,
        QuotedShippingCents = 250,
        CloseTime = now.AddMinutes(-20),
        ClosingWindowEnd = now.AddMinutes(-10),
        CurrentLeaderUserId = userId,
        CurrentLeaderMaxCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        OrderNumber = "MK-SHIP-CHOICE-002",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(12),
        ShippingMode = AuctionShippingModes.Unselected,
        ShippingAmountCents = 0,
        CurrencyCode = "USD",
        SubtotalCents = 1300,
        DiscountTotalCents = 0,
        TotalCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.PostAsJsonAsync(
      $"/api/orders/{orderId}/auction-shipping-choice",
      new SetAuctionShippingChoiceRequest(AuctionShippingModes.OpenBox));

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<AuctionShippingChoiceResponse>();
    dto.Should().NotBeNull();
    dto!.ShippingMode.Should().Be(AuctionShippingModes.OpenBox);
    dto.ShippingAmountCents.Should().Be(0);
    dto.TotalCents.Should().Be(1300);

    await using var verifyScope = factory.Services.CreateAsyncScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var order = await verifyDb.Orders.SingleAsync(o => o.Id == orderId);
    order.ShippingMode.Should().Be(AuctionShippingModes.OpenBox);
    order.ShippingAmountCents.Should().Be(0);
    order.TotalCents.Should().Be(1300);
  }

  [Fact]
  public async Task Ship_now_choice_requires_quoted_shipping()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    Guid userId;
    Guid orderId;
    Guid auctionId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      userId = Guid.NewGuid();
      auctionId = Guid.NewGuid();
      orderId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "missing-quote@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = Guid.NewGuid(),
        Status = "CLOSED_WAITING_ON_PAYMENT",
        StartingPriceCents = 1000,
        CurrentPriceCents = 1300,
        BidCount = 2,
        ReserveMet = true,
        QuotedShippingCents = null,
        CloseTime = now.AddMinutes(-20),
        ClosingWindowEnd = now.AddMinutes(-10),
        CurrentLeaderUserId = userId,
        CurrentLeaderMaxCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        OrderNumber = "MK-SHIP-CHOICE-003",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(12),
        ShippingMode = AuctionShippingModes.Unselected,
        ShippingAmountCents = 0,
        CurrencyCode = "USD",
        SubtotalCents = 1300,
        DiscountTotalCents = 0,
        TotalCents = 1300,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.PostAsJsonAsync(
      $"/api/orders/{orderId}/auction-shipping-choice",
      new SetAuctionShippingChoiceRequest(AuctionShippingModes.ShipNow));

    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body.Should().NotBeNull();
    body!["error"].Should().Be("QUOTED_SHIPPING_REQUIRED");
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}