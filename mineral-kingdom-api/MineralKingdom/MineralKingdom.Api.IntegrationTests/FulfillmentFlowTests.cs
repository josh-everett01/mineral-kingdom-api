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
  public async Task Direct_ship_group_progresses_states_and_audits_transitions()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var userId = Guid.NewGuid();

    var group = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      BoxStatus = "CLOSED",
      ShipmentRequestStatus = "NONE",
      Status = "READY_TO_FULFILL",
      CreatedAt = now,
      UpdatedAt = now
    };

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      ShippingMode = StoreShippingModes.ShipNow,
      FulfillmentGroupId = group.Id,
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      PaymentDueAt = null,
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      ShippingAmountCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10)
    };

    db.FulfillmentGroups.Add(group);
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var packedRes = await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/packed", content: null);
    packedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var shippedReq = new AdminMarkShippedRequest
    {
      ShippingCarrier = "USPS",
      TrackingNumber = "9400TEST123"
    };

    var shippedRes = await client.PostAsJsonAsync($"/api/admin/fulfillment/groups/{group.Id}/shipped", shippedReq);
    shippedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var deliveredRes = await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/delivered", content: null);
    deliveredRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var reloadedGroup = await db.FulfillmentGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
    reloadedGroup.Status.Should().Be("DELIVERED");
    reloadedGroup.PackedAt.Should().NotBeNull();
    reloadedGroup.ShippedAt.Should().NotBeNull();
    reloadedGroup.DeliveredAt.Should().NotBeNull();
    reloadedGroup.ShippingCarrier.Should().Be("USPS");
    reloadedGroup.TrackingNumber.Should().Be("9400TEST123");

    var audits = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "FULFILLMENT_GROUP" && a.EntityId == group.Id)
      .OrderBy(a => a.CreatedAt)
      .ToListAsync();

    audits.Select(a => a.ActionType).Should().ContainInOrder(
      "FULFILLMENT_GROUP_PACKED",
      "FULFILLMENT_GROUP_SHIPPED",
      "FULFILLMENT_GROUP_DELIVERED"
    );
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
  public async Task Direct_ship_group_cannot_ship_unless_packed_and_requires_carrier_and_tracking()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var userId = Guid.NewGuid();

    var group = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      BoxStatus = "CLOSED",
      ShipmentRequestStatus = "NONE",
      Status = "READY_TO_FULFILL",
      CreatedAt = now,
      UpdatedAt = now
    };

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      ShippingMode = StoreShippingModes.ShipNow,
      FulfillmentGroupId = group.Id,
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      ShippingAmountCents = 0,
      TotalCents = 1000
    };

    db.FulfillmentGroups.Add(group);
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var shipRes1 = await client.PostAsJsonAsync(
      $"/api/admin/fulfillment/groups/{group.Id}/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "X" });

    shipRes1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var packRes = await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/packed", content: null);
    packRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var shipRes2 = await client.PostAsJsonAsync(
      $"/api/admin/fulfillment/groups/{group.Id}/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "", TrackingNumber = "X" });

    shipRes2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var shipRes3 = await client.PostAsJsonAsync(
      $"/api/admin/fulfillment/groups/{group.Id}/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "" });

    shipRes3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Idempotent_group_pack_calls_do_not_create_extra_audit_rows()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var userId = Guid.NewGuid();

    var group = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      BoxStatus = "CLOSED",
      ShipmentRequestStatus = "NONE",
      Status = "READY_TO_FULFILL",
      CreatedAt = now,
      UpdatedAt = now
    };

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      ShippingMode = StoreShippingModes.ShipNow,
      FulfillmentGroupId = group.Id,
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10),
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      ShippingAmountCents = 0,
      TotalCents = 1000
    };

    db.FulfillmentGroups.Add(group);
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    (await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/packed", content: null))
      .StatusCode.Should().Be(HttpStatusCode.NoContent);

    (await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/packed", content: null))
      .StatusCode.Should().Be(HttpStatusCode.NoContent);

    var audits = await db.AdminAuditLogs.AsNoTracking()
      .Where(a => a.EntityType == "FULFILLMENT_GROUP" && a.EntityId == group.Id)
      .ToListAsync();

    audits.Count(a => a.ActionType == "FULFILLMENT_GROUP_PACKED").Should().Be(1);
  }

  [Fact]
  public async Task Direct_ship_group_can_be_packed_shipped_and_delivered_without_shipping_invoice()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var userId = Guid.NewGuid();

    var group = new FulfillmentGroup
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      BoxStatus = "CLOSED",
      ShipmentRequestStatus = "NONE",
      Status = "READY_TO_FULFILL",
      CreatedAt = now,
      UpdatedAt = now
    };

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      OrderNumber = $"MK-TEST-{Guid.NewGuid():N}"[..18],
      SourceType = "STORE",
      ShippingMode = StoreShippingModes.ShipNow,
      FulfillmentGroupId = group.Id,
      Status = "READY_TO_FULFILL",
      PaidAt = now.AddMinutes(-5),
      PaymentDueAt = null,
      SubtotalCents = 1000,
      DiscountTotalCents = 0,
      ShippingAmountCents = 0,
      TotalCents = 1000,
      CurrencyCode = "USD",
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-10)
    };

    db.FulfillmentGroups.Add(group);
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = factory.CreateClient();
    AsOwner(client);

    var packedRes = await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/packed", content: null);
    packedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var shippedRes = await client.PostAsJsonAsync(
      $"/api/admin/fulfillment/groups/{group.Id}/shipped",
      new AdminMarkShippedRequest
      {
        ShippingCarrier = "USPS",
        TrackingNumber = "9400DIRECT123"
      });

    shippedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var deliveredRes = await client.PostAsync($"/api/admin/fulfillment/groups/{group.Id}/delivered", content: null);
    deliveredRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var reloadedGroup = await db.FulfillmentGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
    reloadedGroup.Status.Should().Be("DELIVERED");
    reloadedGroup.PackedAt.Should().NotBeNull();
    reloadedGroup.ShippedAt.Should().NotBeNull();
    reloadedGroup.DeliveredAt.Should().NotBeNull();
    reloadedGroup.ShippingCarrier.Should().Be("USPS");
    reloadedGroup.TrackingNumber.Should().Be("9400DIRECT123");

    var invoices = await db.ShippingInvoices.AsNoTracking()
      .Where(i => i.FulfillmentGroupId == group.Id)
      .ToListAsync();

    invoices.Should().BeEmpty();
  }
}