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
}