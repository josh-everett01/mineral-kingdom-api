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

public sealed class EmailOutboxDedupeTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public EmailOutboxDedupeTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsOwner(HttpClient client)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);
  }

  [Fact]
  public async Task Webhook_retry_does_not_create_duplicate_payment_received_email()
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
        Email = "dedupe_user@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-DD-{Guid.NewGuid():N}"[..18],
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

      db.Orders.Add(order);
      await db.SaveChangesAsync();

      orderId = order.Id;
    }

    // Call confirm-paid twice (simulating webhook retries)
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var svc = scope2.ServiceProvider.GetRequiredService<MineralKingdom.Infrastructure.Orders.OrderService>();
      var now = DateTimeOffset.UtcNow;

      (await svc.ConfirmPaidOrderFromWebhookAsync(orderId, "ref-1", now, CancellationToken.None)).Ok.Should().BeTrue();
      (await svc.ConfirmPaidOrderFromWebhookAsync(orderId, "ref-1", now.AddSeconds(1), CancellationToken.None)).Ok.Should().BeTrue();
    }

    await using (var scope3 = factory.Services.CreateAsyncScope())
    {
      var db = scope3.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Best assertion: dedupe key is the contract that prevents duplicates
      var rows = await db.EmailOutbox.AsNoTracking()
        .Where(x =>
          x.TemplateKey == "PAYMENT_RECEIVED" &&
          x.DedupeKey.Contains($"ORDER:{orderId}"))
        .ToListAsync();

      rows.Should().HaveCount(1);
    }
  }

  [Fact]
  public async Task Idempotent_ship_does_not_duplicate_shipment_confirmation_email()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;
    Guid groupId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      userId = Guid.NewGuid();
      db.Users.Add(new User
      {
        Id = userId,
        Email = "dedupe_ship_user@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var group = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        BoxStatus = "CLOSED",
        ClosedAt = now,
        Status = "PACKED",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);

      // Gate requires invoice paid when amount > 0; use zero paid to simplify
      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 0,
        CurrencyCode = "USD",
        Status = "PAID",
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      });

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-DD-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        FulfillmentGroupId = group.Id,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      await db.SaveChangesAsync();

      orderId = order.Id;
      groupId = group.Id;
    }

    using var client = factory.CreateClient();
    AsOwner(client);

    // Ship twice (second is idempotent)
    var req = new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "DUPETEST" };

    (await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/fulfillment/shipped", req)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.PostAsJsonAsync($"/api/admin/orders/{orderId}/fulfillment/shipped", req)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Best assertion: dedupe key is the contract that prevents duplicates
      var rows = await db.EmailOutbox.AsNoTracking()
        .Where(x =>
          x.TemplateKey == "SHIPMENT_CONFIRMED" &&
          x.DedupeKey.Contains($"GROUP:{groupId}"))
        .ToListAsync();

      rows.Should().HaveCount(1);
    }
  }
}