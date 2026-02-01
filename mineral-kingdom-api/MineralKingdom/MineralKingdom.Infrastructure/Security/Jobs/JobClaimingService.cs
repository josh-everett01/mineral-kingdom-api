using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security.Jobs;

public sealed class JobClaimingService
{
  private readonly IDbContextFactory<MineralKingdomDbContext> _dbFactory;

  public JobClaimingService(IDbContextFactory<MineralKingdomDbContext> dbFactory)
  {
    _dbFactory = dbFactory;
  }

  public async Task<List<BackgroundJob>> ClaimDueAsync(
    string workerId,
    int batchSize,
    TimeSpan lockTimeout,
    DateTimeOffset now,
    CancellationToken ct)
  {
    await using var db = await _dbFactory.CreateDbContextAsync(ct);

    // Optional: reclaim stale RUNNING jobs (worker crashed).
    // Treat as a failure so it flows through the normal retry/DLQ semantics.
    var staleBefore = now - lockTimeout;

    await db.Database.ExecuteSqlInterpolatedAsync($@"
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

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    // 1) Lock & select due jobs.
    // NOTE: This SELECT is the key part: FOR UPDATE SKIP LOCKED
    var due = await db.Jobs
      .FromSqlInterpolated($@"
        SELECT *
        FROM jobs
        WHERE ""Status"" IN ('PENDING', 'FAILED')
          AND ""RunAt"" <= {now}
          AND ""CompletedAt"" IS NULL
        ORDER BY ""RunAt"" ASC
        FOR UPDATE SKIP LOCKED
        LIMIT {batchSize};
      ")
      .ToListAsync(ct);

    if (due.Count == 0)
    {
      await tx.CommitAsync(ct);
      return due;
    }

    // 2) Mark as RUNNING + lock metadata
    foreach (var job in due)
    {
      job.Status = JobStatuses.Running;
      job.LockedAt = now;
      job.LockedBy = workerId;
      job.UpdatedAt = now;
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return due;
  }
}
