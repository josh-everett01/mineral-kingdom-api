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

public sealed class ShippingInvoiceOverrideTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public ShippingInvoiceOverrideTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsStaff(HttpClient client)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Staff);
  }

  private static void AsOwner(HttpClient client)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);
  }

  [Fact]
  public async Task Override_sets_isOverride_updates_amount_preserves_calculated_and_audits()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid groupId;
    Guid invoiceId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      groupId = Guid.NewGuid();
      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = Guid.NewGuid(),
        GuestEmail = null,
        BoxStatus = "CLOSED",
        ClosedAt = now,
        Status = "READY_TO_FULFILL",
        CreatedAt = now.AddHours(-1),
        UpdatedAt = now.AddMinutes(-5)
      });

      invoiceId = Guid.NewGuid();
      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = invoiceId,
        FulfillmentGroupId = groupId,
        CalculatedAmountCents = 1200,   // tier calc snapshot
        AmountCents = 1200,             // initial charged amount
        CurrencyCode = "USD",
        Status = "UNPAID",
        PaidAt = null,
        Provider = null,
        ProviderCheckoutId = null,
        ProviderPaymentId = null,
        PaymentReference = null,
        IsOverride = false,
        OverrideReason = null,
        CreatedAt = now.AddMinutes(-10),
        UpdatedAt = now.AddMinutes(-10)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsStaff(client); // overrides are AdminAccess, refunds are OWNER-only (later)

    var req = new AdminOverrideShippingInvoiceRequest(
      AmountCents: 999,
      Reason: "Adjusted shipping due to combined box");

    var res = await client.PostAsJsonAsync($"/api/admin/shipping-invoices/{invoiceId}/override", req);
    res.StatusCode.Should().Be(HttpStatusCode.NoContent);

    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var inv = await db.ShippingInvoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
      inv.IsOverride.Should().BeTrue();
      inv.OverrideReason.Should().Be("Adjusted shipping due to combined box");
      inv.AmountCents.Should().Be(999);
      inv.CalculatedAmountCents.Should().Be(1200); // snapshot preserved

      var audits = await db.AdminAuditLogs.AsNoTracking()
        .Where(a => a.EntityType == "SHIPPING_INVOICE" && a.EntityId == invoiceId)
        .OrderByDescending(a => a.CreatedAt)
        .ToListAsync();

      audits.Should().NotBeEmpty();
      audits[0].ActionType.Should().Be("SHIPPING_INVOICE_OVERRIDE_APPLIED");
      audits[0].BeforeJson.Should().NotBeNull();
      audits[0].AfterJson.Should().NotBeNull();
    }
  }

  [Fact]
  public async Task Override_requires_reason_and_non_negative_amount()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid groupId;
    Guid invoiceId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      groupId = Guid.NewGuid();
      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = Guid.NewGuid(),
        BoxStatus = "CLOSED",
        ClosedAt = now,
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      });

      invoiceId = Guid.NewGuid();
      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = invoiceId,
        FulfillmentGroupId = groupId,
        CalculatedAmountCents = 500,
        AmountCents = 500,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsStaff(client);

    // Missing reason
    var res1 = await client.PostAsJsonAsync(
      $"/api/admin/shipping-invoices/{invoiceId}/override",
      new AdminOverrideShippingInvoiceRequest(AmountCents: 400, Reason: ""));

    res1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Negative amount
    var res2 = await client.PostAsJsonAsync(
      $"/api/admin/shipping-invoices/{invoiceId}/override",
      new AdminOverrideShippingInvoiceRequest(AmountCents: -1, Reason: "bad"));

    res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Override_rejects_paid_invoice_if_guardrail_enabled()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid groupId;
    Guid invoiceId;

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      groupId = Guid.NewGuid();
      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = Guid.NewGuid(),
        BoxStatus = "CLOSED",
        ClosedAt = now,
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      });

      invoiceId = Guid.NewGuid();
      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = invoiceId,
        FulfillmentGroupId = groupId,
        CalculatedAmountCents = 800,
        AmountCents = 800,
        CurrencyCode = "USD",
        Status = "PAID",
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsStaff(client);

    var res = await client.PostAsJsonAsync(
      $"/api/admin/shipping-invoices/{invoiceId}/override",
      new AdminOverrideShippingInvoiceRequest(AmountCents: 700, Reason: "late adjustment"));
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}