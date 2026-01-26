using Microsoft.Extensions.Configuration;

namespace MineralKingdom.Infrastructure.Configuration;

public static class DbConnectionFactory
{
    public static string BuildPostgresConnectionString(IConfiguration config)
    {
        var host = config["MK_DB:HOST"] ?? "localhost";
        var port = config["MK_DB:PORT"] ?? "5432";
        var db = config["MK_DB:NAME"] ?? "mk";
        var user = config["MK_DB:USER"] ?? "mk";
        var pw = config["MK_DB:PASSWORD"] ?? "mk_dev_pw";

        return $"Host={host};Port={port};Database={db};Username={user};Password={pw}";
    }
}
