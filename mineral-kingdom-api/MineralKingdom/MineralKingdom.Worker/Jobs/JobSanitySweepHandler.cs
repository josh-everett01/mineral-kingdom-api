using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Worker.Jobs;

public sealed class JobSanitySweepHandler : IJobHandler
{
  private readonly MineralKingdomDbContext _db;
  private readonly ILogger<JobSanitySweepHandler> _logger;

  // Keep consistent with Worker lockTimeout unless you later make this configurable.
  private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

  public JobSanitySweepHandler(MineralKingdomDbContext db, ILogger<JobSanitySweepHandler> logger)
  {
    _db = db;
    _logger = logger;
  }

  public string Type => "JOB_SANITY_SWEEP";

  public async Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var staleBefore = now - LockTimeout;

    // Reclaim stale RUNNING jobs (same semantics as S2-3 reclaim logic).
    var reclaimed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
      UPDATE jobs
      SET ""Attempts"" = ""Attempts"" + 1,
          ""Status"" = CASE
              WHEN (""Attempts"" + 1) >= ""MaxAttempts"" THEN 'DEAD_LETTER'
              ELSE 'FAILED'
          END,
          ""RunAt"" = {now},
          ""LastError"" = 'STALE_LOCK_TIMEOUT',
          ""LockedAt"" = NULL,
          ""LockedBy"" = NULL,
          ""UpdatedAt"" = {now}
      WHERE ""Status"" = 'RUNNING'
        AND ""LockedAt"" IS NOT NULL
        AND ""LockedAt"" < {staleBefore}
        AND ""CompletedAt"" IS NULL;
    ", ct);

    var duePending = await _db.Jobs.AsNoTracking()
      .CountAsync(j => j.Status == JobStatuses.Pending && j.RunAt <= now && j.CompletedAt == null, ct);

    var dueFailed = await _db.Jobs.AsNoTracking()
      .CountAsync(j => j.Status == JobStatuses.Failed && j.RunAt <= now && j.CompletedAt == null, ct);

    var dlq = await _db.Jobs.AsNoTracking()
      .CountAsync(j => j.Status == JobStatuses.DeadLetter, ct);

    _logger.LogInformation(
      "JOB_SANITY_SWEEP complete. Reclaimed={Reclaimed}. DuePending={DuePending}. DueFailed={DueFailed}. DLQ={DLQ}.",
      reclaimed, duePending, dueFailed, dlq);
  }
}
