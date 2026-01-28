using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
  public string Database { get; } = "mk_test";
  public string Username { get; } = "mk";
  public string Password { get; } = "mk";

  public PostgreSqlContainer Container { get; }

  public PostgresContainerFixture()
  {
    Container = new PostgreSqlBuilder("postgres:16-alpine")
    .WithDatabase(Database)
    .WithUsername(Username)
    .WithPassword(Password)
    .Build();
  }

  public string Host => Container.Hostname;

  public int Port => Container.GetMappedPublicPort(5432);

  public async Task InitializeAsync() => await Container.StartAsync();

  public async Task DisposeAsync() => await Container.DisposeAsync();
}