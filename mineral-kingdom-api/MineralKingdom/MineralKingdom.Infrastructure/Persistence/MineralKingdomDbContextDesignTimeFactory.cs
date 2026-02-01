using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Persistence;

public sealed class MineralKingdomDbContextDesignTimeFactory
  : IDesignTimeDbContextFactory<MineralKingdomDbContext>
{
  public MineralKingdomDbContext CreateDbContext(string[] args)
  {
    // EF runs from different working dirs depending on command, so keep it simple:
    var basePath = Directory.GetCurrentDirectory();

    var config = new ConfigurationBuilder()
      .SetBasePath(basePath)
      .AddJsonFile("appsettings.json", optional: true)
      .AddJsonFile("appsettings.Development.json", optional: true)
      .AddEnvironmentVariables()
      .Build();

    var cs = DbConnectionFactory.BuildPostgresConnectionString(config);

    var opts = new DbContextOptionsBuilder<MineralKingdomDbContext>()
      .UseNpgsql(cs)
      .Options;

    return new MineralKingdomDbContext(opts);
  }
}
