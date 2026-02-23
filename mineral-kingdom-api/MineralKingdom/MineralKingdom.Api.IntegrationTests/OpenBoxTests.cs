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

public sealed class OpenBoxTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public OpenBoxTests(PostgresContainerFixture pg) => _pg = pg;

  private static Guid AsUser(HttpClient client, string role = UserRoles.User, bool emailVerified = true)
  {
    var userId = Guid.NewGuid();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, emailVerified ? "true" : "false");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
    return userId;
  }

  [Fact]
  public async Task Customer_can_create_and_get_open_box_idempotent()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    // Seed user to satisfy EmailVerified policy + ensure user exists
    Guid userId;
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();
      db.Users.Add(new User
      {
        Id = userId,
        Email = "openbox_user@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var r1 = await client.PostAsync("/api/me/open-box", content: null);
    r1.StatusCode.Should().Be(HttpStatusCode.OK);
    var dto1 = await r1.Content.ReadFromJsonAsync<OpenBoxDto>();
    dto1.Should().NotBeNull();
    dto1!.BoxStatus.Should().Be("OPEN");

    var r2 = await client.PostAsync("/api/me/open-box", content: null);
    r2.StatusCode.Should().Be(HttpStatusCode.OK);
    var dto2 = await r2.Content.ReadFromJsonAsync<OpenBoxDto>();
    dto2.Should().NotBeNull();

    dto2!.FulfillmentGroupId.Should().Be(dto1.FulfillmentGroupId);
  }

  [Fact]
  public async Task Customer_can_add_ready_order_to_open_box_and_close_box_blocks_new_adds()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid order1Id;
    Guid order2Id;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "openbox_user2@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var o1 = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-OB-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      };

      var o2 = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-OB-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 2000,
        DiscountTotalCents = 0,
        TotalCents = 2000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.AddRange(o1, o2);
      await db.SaveChangesAsync();

      order1Id = o1.Id;
      order2Id = o2.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    // Create open box
    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);

    // Add order1
    (await client.PostAsync($"/api/me/open-box/orders/{order1Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Close
    (await client.PostAsync("/api/me/open-box/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Attempt to add order2 => should fail
    var add2 = await client.PostAsync($"/api/me/open-box/orders/{order2Id}", null);
    add2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Admin can list open boxes: should not include this one (it's closed)
    using var adminClient = factory.CreateClient();
    AsUser(adminClient, role: UserRoles.Owner, emailVerified: true);

    var list = await adminClient.GetAsync("/api/admin/fulfillment/open-boxes");
    list.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await list.Content.ReadAsStringAsync();
    body.Should().NotContain("OPEN"); // closed box should not show up in open-boxes list
  }

  [Fact]
  public async Task Cannot_add_order_not_ready_to_fulfill()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "openbox_user3@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var o = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-OB-{Guid.NewGuid():N}"[..18],
        SourceType = "AUCTION",
        Status = "AWAITING_PAYMENT",
        PaidAt = null,
        PaymentDueAt = now.AddHours(1),
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(o);
      await db.SaveChangesAsync();
      orderId = o.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);

    var res = await client.PostAsync($"/api/me/open-box/orders/{orderId}", null);
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var json = await res.Content.ReadAsStringAsync();
    json.Should().Contain("ORDER_NOT_READY_TO_FULFILL");
  }

  [Fact]
  public async Task Admin_can_list_open_boxes_and_fetch_group_details_with_orders()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;
    Guid boxId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "openbox_user4@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var box = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        BoxStatus = "OPEN",
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      };

      var o = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-OB-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        FulfillmentGroupId = box.Id,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(box);
      db.Orders.Add(o);
      await db.SaveChangesAsync();

      boxId = box.Id;
      orderId = o.Id;
    }

    using var admin = factory.CreateClient();
    AsUser(admin, role: UserRoles.Owner, emailVerified: true);

    var list = await admin.GetAsync("/api/admin/fulfillment/open-boxes");
    list.StatusCode.Should().Be(HttpStatusCode.OK);

    var listBody = await list.Content.ReadAsStringAsync();
    listBody.Should().Contain(boxId.ToString());

    var detail = await admin.GetAsync($"/api/admin/fulfillment/groups/{boxId}");
    detail.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await detail.Content.ReadFromJsonAsync<OpenBoxDto>();
    dto.Should().NotBeNull();
    dto!.FulfillmentGroupId.Should().Be(boxId);
    dto.BoxStatus.Should().Be("OPEN");
    dto.Orders.Should().Contain(o => o.OrderId == orderId);
  }
}