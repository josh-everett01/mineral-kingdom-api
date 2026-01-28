using System.Threading.Tasks;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public class ApiBootTests
{
  private readonly PostgresContainerFixture _pg;

  public ApiBootTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Api_can_boot_with_container_connection_string()
  {
    await using var factory = new TestAppFactory(
    _pg.Host,
    _pg.Port,
    _pg.Database,
    _pg.Username,
    _pg.Password
);

    // just creating the client forces host startup
    using var client = factory.CreateClient();

    Assert.NotNull(client);
  }
}
