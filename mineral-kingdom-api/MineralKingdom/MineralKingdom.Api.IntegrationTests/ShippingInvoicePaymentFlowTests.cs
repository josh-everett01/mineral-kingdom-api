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
using MineralKingdom.Infrastructure.Payments;
using Xunit;
using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ShippingInvoicePaymentFlowTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public ShippingInvoicePaymentFlowTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsUser(HttpClient client, Guid userId, string role, bool emailVerified = true)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, emailVerified ? "true" : "false");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
  }

  [Fact]
  public async Task Can_start_paypal_payment_for_shipping_invoice_and_webhook_marks_paid()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid groupId;
    Guid invoiceId;
    string? providerCheckoutId;

    // Arrange: user + closed box + unpaid invoice > 0
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_inv_pay_user@example.com",
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
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      };

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.OpenBox,
        FulfillmentGroupId = group.Id,
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1500,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 1500,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);

      var inv = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 899,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.ShippingInvoices.Add(inv);
      await db.SaveChangesAsync();

      groupId = group.Id;
      invoiceId = inv.Id;
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    // Act: start PayPal payment
    var payRes = await client.PostAsJsonAsync("/api/me/open-box/shipping-invoice/pay",
      new CreateShippingInvoicePaymentRequest(
        Provider: PaymentProviders.PayPal,
        SuccessUrl: "https://example.invalid/success",
        CancelUrl: "https://example.invalid/cancel"));

    payRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var redirect = await payRes.Content.ReadFromJsonAsync<CreateShippingInvoicePaymentRedirectResult>();
    redirect.Should().NotBeNull();
    providerCheckoutId = redirect!.ProviderCheckoutId;

    // Assert invoice now has provider checkout id
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var inv = await db.ShippingInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
      inv.Provider.Should().Be(PaymentProviders.PayPal);
      inv.ProviderCheckoutId.Should().Be(providerCheckoutId);
      inv.Status.Should().Be("UNPAID");
    }

    // Simulate PayPal webhook capture completed that references custom_id = invoiceId and order_id = providerCheckoutId
    var webhookJson = $@"
{{
  ""event_type"": ""PAYMENT.CAPTURE.COMPLETED"",
  ""resource"": {{
    ""id"": ""CAPTURE-123"",
    ""custom_id"": ""{invoiceId}"",
    ""invoice_id"": ""{groupId}"",
    ""supplementary_data"": {{
      ""related_ids"": {{
        ""order_id"": ""{providerCheckoutId}""
      }}
    }}
  }}
}}";

    await using (var scope3 = factory.Services.CreateAsyncScope())
    {
      var svc = scope3.ServiceProvider.GetRequiredService<PaymentWebhookService>();
      await svc.ProcessPayPalAsync("EVT-PP-1", webhookJson, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    // Assert paid
    await using (var scope4 = factory.Services.CreateAsyncScope())
    {
      var db = scope4.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var inv = await db.ShippingInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
      inv.Status.Should().Be("PAID");
      inv.PaidAt.Should().NotBeNull();
      inv.ProviderPaymentId.Should().Be("CAPTURE-123");
    }
  }

  [Fact]
  public async Task GetInvoiceDetailForUserAsync_returns_not_found_for_ship_now_group_invoice()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid invoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_now_invoice_hidden@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

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
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.ShipNow,
        FulfillmentGroupId = group.Id,
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 1200,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 1200,
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      };

      var invoice = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 899,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.Orders.Add(order);
      db.ShippingInvoices.Add(invoice);
      await db.SaveChangesAsync();

      invoiceId = invoice.Id;
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<ShippingInvoicePaymentService>();

      var (ok, err, detail) = await svc.GetInvoiceDetailForUserAsync(userId, invoiceId, CancellationToken.None);

      ok.Should().BeFalse();
      err.Should().Be("INVOICE_NOT_FOUND");
      detail.Should().BeNull();
    }
  }

  [Fact]
  public async Task StartForInvoiceAsync_returns_not_found_for_ship_now_group_invoice()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid invoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_now_invoice_start_blocked@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

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
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.ShipNow,
        FulfillmentGroupId = group.Id,
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 1200,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 1200,
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      };

      var invoice = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 899,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.Orders.Add(order);
      db.ShippingInvoices.Add(invoice);
      await db.SaveChangesAsync();

      invoiceId = invoice.Id;
    }

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var svc = scope.ServiceProvider.GetRequiredService<ShippingInvoicePaymentService>();

      var (ok, err, result) = await svc.StartForInvoiceAsync(
        invoiceId,
        userId,
        PaymentProviders.PayPal,
        "https://example.invalid/success",
        "https://example.invalid/cancel",
        DateTimeOffset.UtcNow,
        CancellationToken.None);

      ok.Should().BeFalse();
      err.Should().Be("INVOICE_NOT_FOUND");
      result.Should().BeNull();
    }
  }

  [Fact]
  public async Task Open_box_endpoint_ignores_ship_now_group_and_returns_404_when_only_ship_now_group_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;

      db.Users.Add(new User
      {
        Id = userId,
        Email = "open_box_endpoint_ship_now_only@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

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
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.ShipNow,
        FulfillmentGroupId = group.Id,
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 900,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 900,
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      };

      var invoice = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 899,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.Orders.Add(order);
      db.ShippingInvoices.Add(invoice);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    var res = await client.GetAsync("/api/me/open-box/shipping-invoice");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Open_box_endpoint_prefers_real_open_box_group_when_ship_now_group_also_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid openBoxInvoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;

      db.Users.Add(new User
      {
        Id = userId,
        Email = "open_box_endpoint_prefers_real_group@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      var shipNowGroup = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        BoxStatus = "CLOSED",
        ShipmentRequestStatus = "NONE",
        Status = "READY_TO_FULFILL",
        CreatedAt = now.AddMinutes(-5),
        UpdatedAt = now.AddMinutes(-5)
      };

      var shipNowOrder = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.ShipNow,
        FulfillmentGroupId = shipNowGroup.Id,
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 900,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 900,
        PaidAt = now,
        CreatedAt = now.AddMinutes(-5),
        UpdatedAt = now.AddMinutes(-5)
      };

      var staleInvoice = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = shipNowGroup.Id,
        AmountCents = 899,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now.AddMinutes(-5),
        UpdatedAt = now.AddMinutes(-5)
      };

      var openBoxGroup = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        BoxStatus = "LOCKED_FOR_REVIEW",
        ShipmentRequestStatus = "INVOICED",
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      };

      var openBoxOrder = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        ShippingMode = StoreShippingModes.OpenBox,
        FulfillmentGroupId = openBoxGroup.Id,
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 1500,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        TotalCents = 1500,
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      };

      var openBoxInvoice = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = openBoxGroup.Id,
        AmountCents = 1299,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.AddRange(shipNowGroup, openBoxGroup);
      db.Orders.AddRange(shipNowOrder, openBoxOrder);
      db.ShippingInvoices.AddRange(staleInvoice, openBoxInvoice);
      await db.SaveChangesAsync();

      openBoxInvoiceId = openBoxInvoice.Id;
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    var res = await client.GetAsync("/api/me/open-box/shipping-invoice");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ShippingInvoiceDetailDto>();
    dto.Should().NotBeNull();
    dto!.ShippingInvoiceId.Should().Be(openBoxInvoiceId);
  }
}