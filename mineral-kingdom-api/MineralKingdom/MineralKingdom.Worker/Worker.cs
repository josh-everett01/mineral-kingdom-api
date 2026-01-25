using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MineralKingdom.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MineralKingdom.Worker started.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}