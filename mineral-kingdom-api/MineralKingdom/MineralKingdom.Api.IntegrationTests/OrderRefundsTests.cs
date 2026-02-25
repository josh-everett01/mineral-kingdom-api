using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrderRefundsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public OrderRefundsTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsRole(HttpClient client, string role)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
  }

  [Fact]
  public async Task Staff_cannot_refund_owner_only()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid orderId;
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      orderId = Guid.NewGuid();

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = Guid.NewGuid(),
        GuestEmail = null,
        OrderNumber = $"MK-RF-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsRole(client, UserRoles.Staff);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(AmountCents: 200, Reason: "test", Provider: "STRIPE"));

    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Owner_can_partial_refund_records_reason_and_provider_refund_id_and_audits()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var orderId = Guid.NewGuid();

    var order = new Order
    {
      Id = orderId,
      UserId = null,
      GuestEmail = "refund_partial@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "AUCTION",
      AuctionId = null,
      CheckoutHoldId = null,
      Status = "PAID", // legacy is fine now
      PaidAt = now.AddMinutes(-10),
      PaymentDueAt = null,
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-15),
      UpdatedAt = now.AddMinutes(-15),
      FulfillmentGroupId = null
    };

    db.Orders.Add(order);

    // ✅ Required by refund service: succeeded provider payment exists
    db.OrderPayments.Add(new OrderPayment
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      Provider = PaymentProviders.Stripe,
      Status = CheckoutPaymentStatuses.Succeeded,
      ProviderCheckoutId = "cs_test_refund_partial",
      ProviderPaymentId = "pi_test_refund_partial",
      AmountCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-11),
      UpdatedAt = now.AddMinutes(-11)
    });

    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var res = await client.PostAsJsonAsync(
      $"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(
        AmountCents: 100,
        Reason: "partial refund",
        Provider: "STRIPE"));

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var refund = await db.OrderRefunds.AsNoTracking()
      .Where(r => r.OrderId == orderId)
      .OrderByDescending(r => r.CreatedAt)
      .FirstOrDefaultAsync();

    refund.Should().NotBeNull();
    refund!.AmountCents.Should().Be(100);
    refund.Provider.Should().Be(PaymentProviders.Stripe);
    refund.Reason.Should().Be("partial refund");
    refund.ProviderRefundId.Should().NotBeNullOrWhiteSpace();

    var audit = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "ORDER" && a.EntityId == orderId)
      .OrderByDescending(a => a.CreatedAt)
      .FirstOrDefaultAsync();

    audit.Should().NotBeNull();
    audit!.ActionType.Should().Be("ORDER_REFUNDED_PARTIAL");
  }

  [Fact]
  public async Task Owner_can_full_refund_and_second_refund_is_rejected()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var orderId = Guid.NewGuid();

    var order = new Order
    {
      Id = orderId,
      UserId = null,
      GuestEmail = "refund_full@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "AUCTION",
      AuctionId = null,
      CheckoutHoldId = null,
      Status = "PAID", // legacy is fine now
      PaidAt = now.AddMinutes(-10),
      PaymentDueAt = null,
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-15),
      UpdatedAt = now.AddMinutes(-15),
      FulfillmentGroupId = null
    };

    db.Orders.Add(order);

    // ✅ Required by refund service: succeeded provider payment exists for PAYPAL
    db.OrderPayments.Add(new OrderPayment
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      Provider = PaymentProviders.PayPal,
      Status = CheckoutPaymentStatuses.Succeeded,
      ProviderCheckoutId = "paypal_order_test_full",
      ProviderPaymentId = "paypal_capture_test_full",
      AmountCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-11),
      UpdatedAt = now.AddMinutes(-11)
    });

    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    // Full refund
    var res1 = await client.PostAsJsonAsync(
      $"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(
        AmountCents: 1000,
        Reason: "full refund",
        Provider: "PAYPAL"));

    res1.StatusCode.Should().Be(HttpStatusCode.OK);

    // Second refund should be rejected
    var res2 = await client.PostAsJsonAsync(
      $"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(
        AmountCents: 1,
        Reason: "should fail",
        Provider: "PAYPAL"));

    res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var audit = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "ORDER" && a.EntityId == orderId)
      .OrderBy(a => a.CreatedAt)
      .ToListAsync();

    audit.Select(a => a.ActionType).Should().Contain("ORDER_REFUNDED_FULL");
  }

  [Fact]
  public async Task Cannot_refund_more_than_remaining()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid orderId;
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      orderId = Guid.NewGuid();

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = Guid.NewGuid(),
        GuestEmail = null,
        OrderNumber = $"MK-RF-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      });

      // existing partial refund 600
      db.OrderRefunds.Add(new OrderRefund
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = "STRIPE",
        ProviderRefundId = "existing",
        AmountCents = 600,
        CurrencyCode = "USD",
        Reason = "prior",
        CreatedAt = now.AddMinutes(-1)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsRole(client, UserRoles.Owner);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(AmountCents: 500, Reason: "too much", Provider: "STRIPE"));

    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  private static void AsOwner(HttpClient client)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);
  }

  [Fact]
  public async Task Refunds_allow_legacy_PAID_orders_when_succeeded_order_payment_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    // Arrange: legacy PAID order with a SUCCEEDED Stripe order_payment
    var orderId = Guid.NewGuid();

    var order = new Order
    {
      Id = orderId,
      UserId = null,
      GuestEmail = "paid_legacy_refund@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "AUCTION",
      AuctionId = null,
      CheckoutHoldId = null,
      Status = "PAID", // legacy status we still may have in DB
      PaidAt = now.AddMinutes(-10),
      PaymentDueAt = null,
      SubtotalCents = 1100,
      DiscountTotalCents = 0,
      TotalCents = 1100,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-15),
      UpdatedAt = now.AddMinutes(-15),
      FulfillmentGroupId = null
    };

    db.Orders.Add(order);

    var op = new OrderPayment
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      Provider = "STRIPE",
      Status = CheckoutPaymentStatuses.Succeeded, // key: refundability based on succeeded payment
      ProviderCheckoutId = "cs_test_smoke",
      ProviderPaymentId = "pi_smoke_test",
      AmountCents = 1100,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-9),
      UpdatedAt = now.AddMinutes(-9)
    };

    db.OrderPayments.Add(op);

    await db.SaveChangesAsync();

    // Act: refund 100 cents
    var client = factory.CreateClient();
    AsOwner(client);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new
    {
      amountCents = 100,
      reason = "legacy paid refund test",
      provider = "STRIPE"
    });

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    // Assert: refund row exists
    var refund = await db.OrderRefunds.AsNoTracking()
      .Where(r => r.OrderId == orderId)
      .OrderByDescending(r => r.CreatedAt)
      .FirstOrDefaultAsync();

    refund.Should().NotBeNull();
    refund!.AmountCents.Should().Be(100);
    refund.Provider.Should().Be("STRIPE");
    refund.Reason.Should().Be("legacy paid refund test");

    // Assert: audit row exists
    var audit = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "ORDER" && a.EntityId == orderId)
      .OrderByDescending(a => a.CreatedAt)
      .FirstOrDefaultAsync();

    audit.Should().NotBeNull();
    audit!.ActionType.Should().Be("ORDER_REFUNDED_PARTIAL");
  }
}