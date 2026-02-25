using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Admin.Queues;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AdminQueuesTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AdminQueuesTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsRole(HttpClient client, string role)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
  }

  [Fact]
  public async Task Staff_can_view_queues_and_buckets_match_rules()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    var now = DateTimeOffset.UtcNow;

    Guid awaitingId;
    Guid readyId;
    Guid packedGroupId;
    Guid shippedGroupId;
    Guid openBoxId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      awaitingId = Guid.NewGuid();
      readyId = Guid.NewGuid();

      db.Orders.AddRange(
        new Order
        {
          Id = awaitingId,
          UserId = Guid.NewGuid(),
          GuestEmail = null,
          OrderNumber = $"MK-QA-{Guid.NewGuid():N}"[..18],
          SourceType = "AUCTION",
          Status = "AWAITING_PAYMENT",
          PaymentDueAt = now.AddHours(2),
          CurrencyCode = "USD",
          SubtotalCents = 1000,
          DiscountTotalCents = 0,
          TotalCents = 1000,
          CreatedAt = now.AddMinutes(-10),
          UpdatedAt = now.AddMinutes(-10)
        },
        new Order
        {
          Id = readyId,
          UserId = Guid.NewGuid(),
          GuestEmail = null,
          OrderNumber = $"MK-QR-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now.AddMinutes(-5),
          CurrencyCode = "USD",
          SubtotalCents = 2000,
          DiscountTotalCents = 0,
          TotalCents = 2000,
          CreatedAt = now.AddMinutes(-6),
          UpdatedAt = now.AddMinutes(-5)
        }
      );

      packedGroupId = Guid.NewGuid();
      shippedGroupId = Guid.NewGuid();
      openBoxId = Guid.NewGuid();

      db.FulfillmentGroups.AddRange(
        new FulfillmentGroup
        {
          Id = packedGroupId,
          UserId = Guid.NewGuid(),
          BoxStatus = "CLOSED",
          ClosedAt = now,
          Status = "PACKED",
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddMinutes(-3)
        },
        new FulfillmentGroup
        {
          Id = shippedGroupId,
          UserId = Guid.NewGuid(),
          BoxStatus = "CLOSED",
          ClosedAt = now.AddMinutes(-30),
          Status = "SHIPPED",
          ShippingCarrier = "USPS",
          TrackingNumber = "TRACK",
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddMinutes(-2)
        },
        new FulfillmentGroup
        {
          Id = openBoxId,
          UserId = Guid.NewGuid(),
          BoxStatus = "OPEN",
          ClosedAt = null,
          Status = "READY_TO_FULFILL",
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddMinutes(-1)
        }
      );

      // attach one order to each group for counts
      db.Orders.AddRange(
        new Order
        {
          Id = Guid.NewGuid(),
          UserId = Guid.NewGuid(),
          OrderNumber = $"MK-QP-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now,
          CurrencyCode = "USD",
          SubtotalCents = 500,
          DiscountTotalCents = 0,
          TotalCents = 500,
          FulfillmentGroupId = packedGroupId,
          CreatedAt = now,
          UpdatedAt = now
        },
        new Order
        {
          Id = Guid.NewGuid(),
          UserId = Guid.NewGuid(),
          OrderNumber = $"MK-QS-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now,
          CurrencyCode = "USD",
          SubtotalCents = 600,
          DiscountTotalCents = 0,
          TotalCents = 600,
          FulfillmentGroupId = shippedGroupId,
          CreatedAt = now,
          UpdatedAt = now
        },
        new Order
        {
          Id = Guid.NewGuid(),
          UserId = Guid.NewGuid(),
          OrderNumber = $"MK-QO-{Guid.NewGuid():N}"[..18],
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          PaidAt = now,
          CurrencyCode = "USD",
          SubtotalCents = 700,
          DiscountTotalCents = 0,
          TotalCents = 700,
          FulfillmentGroupId = openBoxId,
          CreatedAt = now,
          UpdatedAt = now
        }
      );

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsRole(client, UserRoles.Staff);

    // awaiting payment
    var awaiting = await client.GetFromJsonAsync<List<AdminQueueOrderDto>>("/api/admin/queues/orders-awaiting-payment");
    awaiting.Should().NotBeNull();
    awaiting!.Select(x => x.OrderId).Should().Contain(awaitingId);
    awaiting.All(x => x.SourceType == "AUCTION" && x.Status == "AWAITING_PAYMENT").Should().BeTrue();

    // ready to fulfill
    var ready = await client.GetFromJsonAsync<List<AdminQueueOrderDto>>("/api/admin/queues/orders-ready-to-fulfill");
    ready.Should().NotBeNull();
    ready!.Select(x => x.OrderId).Should().Contain(readyId);
    ready.All(x => x.Status == "READY_TO_FULFILL").Should().BeTrue();

    // packed
    var packed = await client.GetFromJsonAsync<List<AdminQueueFulfillmentGroupDto>>("/api/admin/queues/fulfillment-packed");
    packed.Should().NotBeNull();
    packed!.Select(x => x.FulfillmentGroupId).Should().Contain(packedGroupId);
    packed.First(x => x.FulfillmentGroupId == packedGroupId).OrderCount.Should().Be(1);

    // shipped
    var shipped = await client.GetFromJsonAsync<List<AdminQueueFulfillmentGroupDto>>("/api/admin/queues/fulfillment-shipped");
    shipped.Should().NotBeNull();
    shipped!.Select(x => x.FulfillmentGroupId).Should().Contain(shippedGroupId);
    shipped.First(x => x.FulfillmentGroupId == shippedGroupId).OrderCount.Should().Be(1);

    // open boxes
    var open = await client.GetFromJsonAsync<List<AdminQueueOpenBoxDto>>("/api/admin/queues/open-boxes");
    open.Should().NotBeNull();
    open!.Select(x => x.FulfillmentGroupId).Should().Contain(openBoxId);
    open.First(x => x.FulfillmentGroupId == openBoxId).OrderCount.Should().Be(1);
  }

  [Fact]
  public async Task User_cannot_view_admin_queues()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();
    AsRole(client, UserRoles.User);

    var res = await client.GetAsync("/api/admin/queues/orders-ready-to-fulfill");
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }
}