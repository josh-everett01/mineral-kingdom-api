using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Worker.Jobs;

public sealed class JobRetrySweepHandler : IJobHandler
{
  private readonly MineralKingdomDbContext _db;
  private readonly ILogger<JobRetrySweepHandler> _logger;

  public JobRetrySweepHandler(MineralKingdomDbContext db, ILogger<JobRetrySweepHandler> logger)
  {
    _db = db;
    _logger = logger;
  }

  public string Type => "JOB_RETRY_SWEEP";

  public async Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var dueFailed = await _db.Jobs.AsNoTracking()
      .CountAsync(j => j.Status == JobStatuses.Failed && j.RunAt <= now && j.CompletedAt == null, ct);

    var duePending = await _db.Jobs.AsNoTracking()
      .CountAsync(j => j.Status == JobStatuses.Pending && j.RunAt <= now && j.CompletedAt == null, ct);

    _logger.LogInformation(
      "JOB_RETRY_SWEEP complete. DueFailed={DueFailed}. DuePending={DuePending}.",
      dueFailed, duePending);
  }
}
