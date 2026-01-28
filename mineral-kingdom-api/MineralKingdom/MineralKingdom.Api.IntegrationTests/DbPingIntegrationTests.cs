using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public class DbPingIntegrationTests
{
  private readonly PostgresContainerFixture _pg;

  public DbPingIntegrationTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Post_then_get_returns_incremented_count()
  {
    await using var factory = new TestAppFactory(
    _pg.Host,
    _pg.Port,
    _pg.Database,
    _pg.Username,
    _pg.Password
);

    // ✅ apply migrations to the container DB
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    using var client = factory.CreateClient();

    // ✅ write
    var postResp = await client.PostAsJsonAsync("/db-ping", new { });
    postResp.EnsureSuccessStatusCode();

    // ✅ read
    var getResp = await client.GetFromJsonAsync<DbPingCountResponse>("/db-ping");
    Assert.NotNull(getResp);
    Assert.True(getResp!.Count >= 1);
  }

  private sealed class DbPingCountResponse
  {
    public int Count { get; set; }
  }
}