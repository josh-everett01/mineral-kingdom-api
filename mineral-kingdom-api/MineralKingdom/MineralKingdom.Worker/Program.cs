using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MineralKingdom.Infrastructure.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Security;
using MineralKingdom.Infrastructure.Security.Jobs;
using MineralKingdom.Worker.Cron;
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

#if DEBUG
              services.AddScoped<AlwaysFailJobHandler>();
#endif

              services.AddSingleton<CronSweepEnqueuer>();
              services.AddHostedService<CronSweepHostedService>();
              services.AddScoped<JobSanitySweepHandler>();
              services.AddScoped<JobRetrySweepHandler>();
              services.AddScoped<AuctionClosingSweepJob>();
              services.AddScoped<AuctionBiddingService>();
              services.AddScoped<AuctionStateMachineService>();
              services.AddSingleton<IAuctionRealtimePublisher, NoopAuctionRealtimePublisher>();
              services.AddScoped<EmailDispatchJobHandler>();
              services.AddScoped<IMKEmailSender, DevNullEmailSender>();


              services.AddHostedService<Worker>();
          })
                .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();

            // Reduce EF noise
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddFilter("Microsoft", LogLevel.Warning);


            // Keep your app logs visible
            logging.AddFilter("MineralKingdom", LogLevel.Information);
        })
          .Build();

        host.Run();
    }
}
