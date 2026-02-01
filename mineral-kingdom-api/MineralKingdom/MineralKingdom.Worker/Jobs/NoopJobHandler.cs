using Microsoft.Extensions.Logging;

namespace MineralKingdom.Worker.Jobs;

public sealed class NoopJobHandler : IJobHandler
{
  private readonly ILogger<NoopJobHandler> _logger;

  public NoopJobHandler(ILogger<NoopJobHandler> logger)
  {
    _logger = logger;
  }

  public string Type => "NOOP";

  public Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    _logger.LogInformation("NOOP job executed. JobId={JobId}", jobId);
    return Task.CompletedTask;
  }
}