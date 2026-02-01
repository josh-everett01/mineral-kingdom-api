using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Security.Jobs;
using MineralKingdom.Worker.Jobs;

namespace MineralKingdom.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
          .ConfigureServices((ctx, services) =>
          {
              services.AddPooledDbContextFactory<MineralKingdomDbContext>(options =>
          {
              var cs = DbConnectionFactory.BuildPostgresConnectionString(ctx.Configuration);
              options.UseNpgsql(cs);
          });

              // ✅ makes MineralKingdomDbContext injectable (scoped) for Worker.cs and any services that still expect it
              services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<MineralKingdomDbContext>>().CreateDbContext());

              services.AddSingleton<JobHandlerRegistry>();
              services.AddScoped<JobClaimingService>();
              services.AddScoped<NoopJobHandler>();
              services.AddHostedService<Worker>();
          })
          .Build();

        host.Run();
    }
}
