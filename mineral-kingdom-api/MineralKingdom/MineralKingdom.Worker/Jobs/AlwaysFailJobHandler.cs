#if DEBUG
using Microsoft.Extensions.Logging;

namespace MineralKingdom.Worker.Jobs;

/// <summary>
/// DEBUG-only handler used for local smoke testing retries -> DLQ.
/// Not compiled into Release builds.
/// </summary>
public sealed class AlwaysFailJobHandler : IJobHandler
{
  private readonly ILogger<AlwaysFailJobHandler> _logger;

  public AlwaysFailJobHandler(ILogger<AlwaysFailJobHandler> logger)
  {
    _logger = logger;
  }

  public string Type => "ALWAYS_FAIL";

  public Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    _logger.LogWarning("ALWAYS_FAIL job executing and will throw. JobId={JobId}", jobId);
    throw new InvalidOperationException("FORCED_FAILURE");
  }
}
#endif
