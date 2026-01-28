using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
}
