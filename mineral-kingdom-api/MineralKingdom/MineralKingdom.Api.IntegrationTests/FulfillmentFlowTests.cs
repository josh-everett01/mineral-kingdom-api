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

public sealed class FulfillmentFlowTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public FulfillmentFlowTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsOwner(HttpClient client)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);
  }

  [Fact]
  public async Task Happy_path_creates_group_progresses_states_and_audits_transitions()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = null,
      GuestEmail = "guest@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      AuctionId = null,
      CheckoutHoldId = null,
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      PaymentDueAt = null,
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      FulfillmentGroupId = null
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var packedRes = await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/packed", content: null);
    packedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var shippedReq = new AdminMarkShippedRequest
    {
      ShippingCarrier = "USPS",
      TrackingNumber = "9400TEST123"
    };

    var shippedRes = await client.PostAsJsonAsync($"/api/admin/orders/{order.Id}/fulfillment/shipped", shippedReq);
    shippedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var deliveredRes = await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/delivered", content: null);
    deliveredRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var reloadedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
    reloadedOrder.FulfillmentGroupId.Should().NotBeNull();

    var groupId = reloadedOrder.FulfillmentGroupId!.Value;

    var group = await db.FulfillmentGroups.AsNoTracking().SingleAsync(g => g.Id == groupId);
    group.Status.Should().Be("DELIVERED");
    group.PackedAt.Should().NotBeNull();
    group.ShippedAt.Should().NotBeNull();
    group.DeliveredAt.Should().NotBeNull();
    group.ShippingCarrier.Should().Be("USPS");
    group.TrackingNumber.Should().Be("9400TEST123");

    var audits = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "FULFILLMENT_GROUP" && a.EntityId == groupId)
      .OrderBy(a => a.CreatedAt)
      .ToListAsync();

    audits.Select(a => a.ActionType).Should().ContainInOrder(
      "ORDER_FULFILLMENT_PACKED",
      "ORDER_FULFILLMENT_SHIPPED",
      "ORDER_FULFILLMENT_DELIVERED"
    );

    audits.Should().HaveCount(3);
    audits.All(a => a.BeforeJson is not null && a.AfterJson is not null).Should().BeTrue();
  }

  [Fact]
  public async Task Cannot_pack_when_order_is_awaiting_payment_and_no_audit_written()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = null,
      GuestEmail = "guest@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "AUCTION",
      AuctionId = null,
      CheckoutHoldId = null,
      Status = "AWAITING_PAYMENT",
      PaidAt = null,
      PaymentDueAt = now.AddHours(2),
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      FulfillmentGroupId = null
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var res = await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/packed", content: null);
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var reloaded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
    reloaded.FulfillmentGroupId.Should().BeNull();

    // jsonb-safe: filter in memory (Postgres can't LIKE jsonb)
    var orderIdStr = order.Id.ToString();

    var audits = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "FULFILLMENT_GROUP")
      .ToListAsync();

    var auditsForOrder = audits
      .Where(a => (a.BeforeJson?.Contains(orderIdStr) ?? false) ||
                  (a.AfterJson?.Contains(orderIdStr) ?? false))
      .ToList();

    auditsForOrder.Should().BeEmpty();
  }

  [Fact]
  public async Task Cannot_ship_unless_packed_and_requires_carrier_and_tracking()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = null,
      GuestEmail = "guest@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var shipRes1 = await client.PostAsJsonAsync(
      $"/api/admin/orders/{order.Id}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "X" });

    shipRes1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var packRes = await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/packed", content: null);
    packRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var shipRes2 = await client.PostAsJsonAsync(
      $"/api/admin/orders/{order.Id}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "", TrackingNumber = "X" });

    shipRes2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var shipRes3 = await client.PostAsJsonAsync(
      $"/api/admin/orders/{order.Id}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "" });

    shipRes3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Idempotent_calls_do_not_create_extra_audit_rows()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = null,
      GuestEmail = "guest@example.com",
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      TotalCents = 1000
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    (await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/packed", content: null))
      .StatusCode.Should().Be(HttpStatusCode.NoContent);

    (await client.PostAsync($"/api/admin/orders/{order.Id}/fulfillment/packed", content: null))
      .StatusCode.Should().Be(HttpStatusCode.NoContent);

    var reloadedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
    reloadedOrder.FulfillmentGroupId.Should().NotBeNull();

    var groupId = reloadedOrder.FulfillmentGroupId!.Value;

    var audits = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "FULFILLMENT_GROUP" && a.EntityId == groupId)
      .ToListAsync();

    audits.Count(a => a.ActionType == "ORDER_FULFILLMENT_PACKED").Should().Be(1);
  }
}