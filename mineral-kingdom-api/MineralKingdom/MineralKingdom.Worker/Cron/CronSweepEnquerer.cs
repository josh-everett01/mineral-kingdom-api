using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Worker.Jobs;

namespace MineralKingdom.Worker.Cron;

public sealed class CronSweepEnqueuer
{
  public const string JobSanitySweepType = "JOB_SANITY_SWEEP";
  public const string JobRetrySweepType = "JOB_RETRY_SWEEP";


  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly IDbContextFactory<MineralKingdomDbContext> _dbFactory;

  public CronSweepEnqueuer(IDbContextFactory<MineralKingdomDbContext> dbFactory)
  {
    _dbFactory = dbFactory;
  }

  public async Task EnqueueDueSweepsAsync(DateTimeOffset now, CancellationToken ct = default)
  {
    var bucketStart = TruncateToBucket(now, bucketSeconds: 60);
    var bucketIso = bucketStart.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:00'Z'");

    await EnqueueOncePerBucketAsync(JobSanitySweepType, bucketIso, now, ct);
    await EnqueueOncePerBucketAsync(JobRetrySweepType, bucketIso, now, ct);
    await EnqueueOncePerBucketAsync(JobTypes.AuctionClosingSweep, bucketIso, now, ct);

    // Nightly analytics snapshot (previous UTC day)
    var prevDay = now.UtcDateTime.Date.AddDays(-1);
    var dayBucket = prevDay.ToString("yyyy-MM-dd");
    await EnqueueOncePerBucketAsync(JobTypes.AnalyticsDailySnapshot, dayBucket, now, ct);
  }

  private async Task EnqueueOncePerBucketAsync(string jobType, string bucketIso, DateTimeOffset runAt, CancellationToken ct)
  {
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);

    // Advisory lock prevents race if multiple schedulers exist.
    // We still do an existence check to prevent duplicates across ticks within the same bucket.
    var lockKey = $"{jobType}:{bucketIso}";

    var locked = await TryAdvisoryXactLockAsync(db, lockKey, ct);
    if (!locked)
    {
      await tx.CommitAsync(ct);
      return;
    }

    var exists = await SweepJobExistsAsync(db, jobType, bucketIso, ct);
    if (exists)
    {
      await tx.CommitAsync(ct);
      return;
    }

    var payload = new
    {
      bucket = bucketIso,
      kind = jobType
    };

    var now = DateTimeOffset.UtcNow;
    var job = new BackgroundJob
    {
      Id = Guid.NewGuid(),
      Type = jobType,
      Status = JobStatuses.Pending,
      Attempts = 0,
      MaxAttempts = 8,
      RunAt = runAt,
      PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Jobs.Add(job);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
  }

  private static DateTimeOffset TruncateToBucket(DateTimeOffset now, int bucketSeconds)
  {
    var unix = now.ToUnixTimeSeconds();
    var bucket = (unix / bucketSeconds) * bucketSeconds;
    return DateTimeOffset.FromUnixTimeSeconds(bucket);
  }

  private static async Task<bool> TryAdvisoryXactLockAsync(MineralKingdomDbContext db, string key, CancellationToken ct)
  {
    await EnsureOpenAsync(db, ct);

    await using var cmd = db.Database.GetDbConnection().CreateCommand();
    cmd.CommandText = "SELECT pg_try_advisory_xact_lock(hashtext(@k)::bigint);";
    AddParam(cmd, "k", key);

    var result = await cmd.ExecuteScalarAsync(ct);
    return result is bool b && b;
  }

  private static async Task<bool> SweepJobExistsAsync(MineralKingdomDbContext db, string jobType, string bucketIso, CancellationToken ct)
  {
    await EnsureOpenAsync(db, ct);

    await using var cmd = db.Database.GetDbConnection().CreateCommand();
    cmd.CommandText = """
      SELECT EXISTS(
        SELECT 1
        FROM jobs
        WHERE "Type" = @t
          AND "PayloadJson" IS NOT NULL
          AND "PayloadJson"->>'bucket' = @b
          AND "CompletedAt" IS NULL
      );
      """;
    AddParam(cmd, "t", jobType);
    AddParam(cmd, "b", bucketIso);

    var result = await cmd.ExecuteScalarAsync(ct);
    return result is bool b && b;
  }

  private static async Task EnsureOpenAsync(MineralKingdomDbContext db, CancellationToken ct)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
      await conn.OpenAsync(ct);
  }

  private static void AddParam(DbCommand cmd, string name, object value)
  {
    var p = cmd.CreateParameter();
    p.ParameterName = name;
    p.Value = value;
    cmd.Parameters.Add(p);
  }
}
