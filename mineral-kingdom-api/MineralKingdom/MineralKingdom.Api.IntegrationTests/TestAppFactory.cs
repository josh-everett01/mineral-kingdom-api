using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class TestAppFactory : WebApplicationFactory<MineralKingdom.Api.Program>
{
  private readonly string _host;
  private readonly string _port;
  private readonly string _db;
  private readonly string _user;
  private readonly string _password;

  public TestAppFactory(string host, int port, string database, string username, string password)
  {
    _host = host;
    _port = port.ToString();
    _db = database;
    _user = username;
    _password = password;
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    builder.ConfigureAppConfiguration((context, configBuilder) =>
    {
      var overrides = new Dictionary<string, string?>
      {
        ["MK_DB:HOST"] = _host,
        ["MK_DB:PORT"] = _port,
        ["MK_DB:NAME"] = _db,
        ["MK_DB:USER"] = _user,
        ["MK_DB:PASSWORD"] = _password,

        ["MK_JWT:Issuer"] = "mk-test",
        ["MK_JWT:Audience"] = "mk-test",
        ["MK_JWT:SigningKey"] = "mk-test-signing-key-change-me-please-1234567890",

        // optional: avoids any PUBLIC_URL surprises in tests
        ["MK_APP:PUBLIC_URL"] = "http://localhost:3000",

        // helpful: makes sure exceptions are verbose
        ["ASPNETCORE_DETAILEDERRORS"] = "true",
      };

      configBuilder.AddInMemoryCollection(overrides);
    });

    builder.ConfigureLogging(logging =>
    {
      logging.ClearProviders();
      logging.AddConsole();
      logging.SetMinimumLevel(LogLevel.Debug);
    });

    // ✅ This is the important part:
    builder.ConfigureServices(services =>
    {
      using var sp = services.BuildServiceProvider();
      using var scope = sp.CreateScope();

      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Database.Migrate();
    });
  }
}
