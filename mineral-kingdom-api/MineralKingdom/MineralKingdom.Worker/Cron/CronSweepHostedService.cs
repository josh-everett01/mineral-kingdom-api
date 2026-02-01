using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MineralKingdom.Worker.Cron;

public sealed class CronSweepHostedService : BackgroundService
{
  private readonly IServiceProvider _services;
  private readonly ILogger<CronSweepHostedService> _logger;
  private readonly CronSweepSettings _settings;

  public CronSweepHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<CronSweepHostedService> logger)
  {
    _services = services;
    _logger = logger;
    _settings = CronSweepSettings.FromConfiguration(configuration);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_settings.Enabled)
    {
      _logger.LogInformation("Cron sweeps disabled.");
      return;
    }

    _logger.LogInformation("Cron sweeps enabled. TickSeconds={TickSeconds}", _settings.TickSeconds);

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.TickSeconds));

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await TickAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Cron sweep tick failed");
      }

      await timer.WaitForNextTickAsync(stoppingToken);
    }
  }

  private async Task TickAsync(CancellationToken ct)
  {
    await using var scope = _services.CreateAsyncScope();
    var enqueuer = scope.ServiceProvider.GetRequiredService<CronSweepEnqueuer>();

    var now = DateTimeOffset.UtcNow;
    await enqueuer.EnqueueDueSweepsAsync(now, ct);
  }
}
