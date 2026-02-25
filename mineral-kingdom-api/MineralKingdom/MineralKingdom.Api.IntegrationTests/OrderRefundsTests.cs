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
    AsRole(client, UserRoles.Owner);

    var res = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(AmountCents: 250, Reason: "customer request", Provider: "STRIPE"));

    res.StatusCode.Should().Be(HttpStatusCode.OK);

    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var refund = await db.OrderRefunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
      refund.AmountCents.Should().Be(250);
      refund.Reason.Should().Be("customer request");
      refund.Provider.Should().Be("STRIPE");
      refund.ProviderRefundId.Should().NotBeNullOrWhiteSpace();

      var audit = await db.AdminAuditLogs.AsNoTracking()
        .OrderByDescending(a => a.CreatedAt)
        .FirstOrDefaultAsync(a => a.EntityType == "ORDER" && a.EntityId == orderId);

      audit.Should().NotBeNull();
      audit!.ActionType.Should().Be("ORDER_REFUNDED_PARTIAL");
      audit.AfterJson.Should().Contain("providerRefundId");
    }
  }

  [Fact]
  public async Task Owner_can_full_refund_and_second_refund_is_rejected()
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
    AsRole(client, UserRoles.Owner);

    // full refund = total (1000)
    (await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(AmountCents: 1000, Reason: "full refund", Provider: "PAYPAL")))
      .StatusCode.Should().Be(HttpStatusCode.OK);

    // attempt second refund should fail
    var res2 = await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds",
      new AdminCreateRefundRequest(AmountCents: 1, Reason: "should fail", Provider: "PAYPAL"));

    res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var refunds = await db.OrderRefunds.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
      refunds.Should().HaveCount(1);

      var audit = await db.AdminAuditLogs.AsNoTracking()
        .Where(a => a.EntityType == "ORDER" && a.EntityId == orderId)
        .OrderByDescending(a => a.CreatedAt)
        .FirstAsync();

      audit.ActionType.Should().Be("ORDER_REFUNDED_FULL");
    }
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
}