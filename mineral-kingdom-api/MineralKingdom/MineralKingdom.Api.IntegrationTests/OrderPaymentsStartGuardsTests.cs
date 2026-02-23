using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrderPaymentsStartGuardsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public OrderPaymentsStartGuardsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Start_payment_for_paid_auction_order_returns_409_and_does_not_create_new_payment()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    // Arrange: create a verified user + JWT bearer token
    Guid userId;
    string accessToken;

    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
      var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();

      var utc = DateTime.UtcNow;         // DateTime (Kind=Utc)
      var now = DateTimeOffset.UtcNow;   // DateTimeOffset


      var user = new User
      {
        Id = Guid.NewGuid(),
        Email = "paid_order_user@example.com",
        EmailVerified = true,
        Role = UserRoles.Owner,
        CreatedAt = utc,
        UpdatedAt = utc
      };
      user.PasswordHash = hasher.HashPassword(user, "Str0ngPass!123");
      db.Users.Add(user);

      var auction = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = Guid.NewGuid(),
        Status = MineralKingdom.Contracts.Auctions.AuctionStatuses.ClosedPaid,

        CreatedAt = now,
        UpdatedAt = now,

        BidCount = 1,

        CloseTime = DateTime.SpecifyKind(utc.AddMinutes(-10), DateTimeKind.Utc),
        StartTime = DateTime.SpecifyKind(utc.AddMinutes(-20), DateTimeKind.Utc),

        // This one is DateTimeOffset in your DB/model (keep as DateTimeOffset)
        ClosingWindowEnd = now.AddMinutes(-9),

        CurrentPriceCents = 1100,
        StartingPriceCents = 1000,
        ReserveMet = false
      };

      db.Auctions.Add(auction);

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        SourceType = "AUCTION",
        AuctionId = auction.Id,
        Status = "READY_TO_FULFILL",
        PaidAt = now.AddMinutes(-1),
        OrderNumber = "MK-TEST-PAID-001",
        CurrencyCode = "USD",
        SubtotalCents = 1100,
        DiscountTotalCents = 0,
        TotalCents = 1100,
        CreatedAt = now.AddMinutes(-2),
        UpdatedAt = now.AddMinutes(-1),
      };
      db.Orders.Add(order);

      db.OrderPayments.Add(new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Provider = MineralKingdom.Contracts.Store.PaymentProviders.PayPal,
        Status = "SUCCEEDED",
        ProviderCheckoutId = "TEST-PAYPAL-ORDER",
        ProviderPaymentId = "TEST-CAPTURE",
        AmountCents = 1100,
        CurrencyCode = "USD",
        CreatedAt = now.AddMinutes(-2),
        UpdatedAt = now.AddMinutes(-1),
      });

      await db.SaveChangesAsync();


      userId = user.Id;
      orderId = order.Id;

      // Mint JWT Bearer token the same way the API would
      var (token, _) = jwt.CreateAccessToken(user, utc);
      accessToken = token;
    }

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    // Capture payment count before
    int beforeCount;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      beforeCount = await db.OrderPayments.CountAsync(p => p.OrderId == orderId);
    }

    // Act
    var req = new StartOrderPaymentRequest(
      Provider: MineralKingdom.Contracts.Store.PaymentProviders.PayPal,
      SuccessUrl: "https://example.com/success",
      CancelUrl: "https://example.com/cancel"
    );

    var res = await client.PostAsJsonAsync($"/api/orders/{orderId}/payments/start", req);

    // Assert: 409 with error code
    res.StatusCode.Should().Be(HttpStatusCode.Conflict);

    var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body.Should().NotBeNull();
    body!.Should().ContainKey("error");
    body["error"].Should().Be("ORDER_NOT_AWAITING_PAYMENT");

    // Assert: did not create new order_payments row
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var afterCount = await db.OrderPayments.CountAsync(p => p.OrderId == orderId);
      afterCount.Should().Be(beforeCount);
    }
  }
}
