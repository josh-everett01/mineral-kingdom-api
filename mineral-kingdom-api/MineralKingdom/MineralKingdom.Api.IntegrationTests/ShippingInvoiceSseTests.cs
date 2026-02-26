using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ShippingInvoiceSseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public ShippingInvoiceSseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Shipping_invoice_sse_requires_auth()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/shipping-invoices/{Guid.NewGuid()}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Shipping_invoice_sse_forbids_non_owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();
    var otherUserId = Guid.NewGuid();

    Guid invoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var group = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        CreatedAt = now,
        UpdatedAt = now
      };

      var inv = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 599,
        CalculatedAmountCents = 599,
        CurrencyCode = "USD",
        Status = "UNPAID",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.ShippingInvoices.Add(inv);
      await db.SaveChangesAsync();

      invoiceId = inv.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, otherUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/shipping-invoices/{invoiceId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Shipping_invoice_sse_emits_initial_snapshot_for_owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();

    Guid invoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var group = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        CreatedAt = now,
        UpdatedAt = now
      };

      var inv = new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = group.Id,
        AmountCents = 0,
        CalculatedAmountCents = 0,
        CurrencyCode = "USD",
        Status = "PAID",
        PaidAt = now,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.ShippingInvoices.Add(inv);
      await db.SaveChangesAsync();

      invoiceId = inv.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/shipping-invoices/{invoiceId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();

    var sb = new StringBuilder();
    var buffer = new byte[1024];
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    while (!timeoutCts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
      if (n <= 0) break;

      sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
      if (sb.ToString().Contains("data:", StringComparison.OrdinalIgnoreCase))
        break;
    }

    var all = sb.ToString();
    all.Should().Contain("event: snapshot");
    all.Should().Contain(invoiceId.ToString());
  }
}