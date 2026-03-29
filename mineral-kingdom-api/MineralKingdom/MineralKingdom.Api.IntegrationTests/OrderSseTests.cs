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

public sealed class OrderSseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public OrderSseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Order_sse_requires_auth()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{Guid.NewGuid()}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Order_sse_forbids_non_owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();
    var otherUserId = Guid.NewGuid();

    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        OrderNumber = "ORDER-SSE-1",
        SourceType = "AUCTION",
        Status = "READY_TO_FULFILL",
        TotalCents = 1234,
        CurrencyCode = "USD",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      await db.SaveChangesAsync();
      orderId = order.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, otherUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Order_sse_emits_initial_snapshot_for_owner_with_richer_payload()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();

    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        OrderNumber = "ORDER-SSE-2",
        SourceType = "AUCTION",
        AuctionId = Guid.NewGuid(),
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        PaymentDueAt = now.AddHours(12),
        FulfillmentGroupId = null,
        TotalCents = 4321,
        CurrencyCode = "USD",
        CreatedAt = now,
        UpdatedAt = now
      };

      var payment = new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Provider = "STRIPE",
        Status = "SUCCEEDED",
        ProviderCheckoutId = "sess_test_123",
        ProviderPaymentId = "pi_test_123",
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      db.OrderPayments.Add(payment);

      await db.SaveChangesAsync();
      orderId = order.Id;
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}/events");
    var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

    await using var stream = await res.Content.ReadAsStreamAsync();

    var sb = new StringBuilder();
    var buffer = new byte[2048];
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    while (!timeoutCts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
      if (n <= 0) break;

      sb.Append(Encoding.UTF8.GetString(buffer, 0, n));

      var text = sb.ToString();
      if (text.Contains("event: snapshot", StringComparison.OrdinalIgnoreCase) &&
          text.Contains("data:", StringComparison.OrdinalIgnoreCase))
      {
        break;
      }
    }

    var all = sb.ToString();

    all.Should().Contain("event: snapshot");
    all.Should().Contain(orderId.ToString());
    all.Should().Contain("ORDER-SSE-2");
    all.Should().Contain("READY_TO_FULFILL");
    all.Should().Contain("STRIPE");
    all.Should().Contain("SUCCEEDED");
    all.Should().Contain("USD");
    all.Should().Contain("4321");

    // NewTimelineEntries is intentionally null for now in the publisher.
    all.Should().Contain("NewTimelineEntries");
  }
}