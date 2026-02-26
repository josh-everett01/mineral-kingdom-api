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

public sealed class FulfillmentSseBroadcastTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public FulfillmentSseBroadcastTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Fulfillment_sse_receives_update_after_admin_marks_packed()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var now = DateTimeOffset.UtcNow;

    var memberUserId = Guid.NewGuid();
    var adminUserId = Guid.NewGuid();

    Guid groupId;
    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var group = new FulfillmentGroup
      {
        Id = Guid.NewGuid(),
        UserId = memberUserId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        CreatedAt = now,
        UpdatedAt = now
      };

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = memberUserId,
        OrderNumber = "ORDER-FULFILL-SSE-1",
        Status = "READY_TO_FULFILL",
        TotalCents = 1111,
        CurrencyCode = "USD",
        FulfillmentGroupId = group.Id,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.FulfillmentGroups.Add(group);
      db.Orders.Add(order);
      await db.SaveChangesAsync();

      groupId = group.Id;
      orderId = order.Id;
    }

    // Connect SSE as the member (owner)
    using var memberClient = factory.CreateClient();
    memberClient.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, memberUserId.ToString());
    memberClient.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    memberClient.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    using var sseReq = new HttpRequestMessage(HttpMethod.Get, $"/api/fulfillment-groups/{groupId}/events");
    var sseRes = await memberClient.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead);
    sseRes.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var stream = await sseRes.Content.ReadAsStreamAsync();

    // Read initial snapshot chunk
    var buf = new byte[4096];
    _ = await stream.ReadAsync(buf);

    // Trigger admin transition
    using var adminClient = factory.CreateClient();
    adminClient.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, adminUserId.ToString());
    adminClient.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    adminClient.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    var packedRes = await adminClient.PostAsync($"/api/admin/orders/{orderId}/fulfillment/packed", content: null);
    packedRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Read until we see PACKED (or timeout)
    var sb = new StringBuilder();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    while (!cts.IsCancellationRequested)
    {
      var n = await stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
      if (n <= 0) break;

      sb.Append(Encoding.UTF8.GetString(buf, 0, n));
      var text = sb.ToString();

      if (text.Contains("\"Status\":\"PACKED\"", StringComparison.OrdinalIgnoreCase))
        break;
    }

    sb.ToString().Should().Contain("event: snapshot");
    sb.ToString().Should().Contain("\"Status\":\"PACKED\"");
  }
}