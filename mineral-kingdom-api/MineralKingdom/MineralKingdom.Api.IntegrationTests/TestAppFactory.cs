using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
      };

      configBuilder.AddInMemoryCollection(overrides);
    });
  }
}