using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security.Jobs;
using MineralKingdom.Worker.Cron;
using MineralKingdom.Worker.Jobs;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CronSweepsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CronSweepsTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Cron_enqueues_sweeps_only_once_per_bucket()
  {
    using var sp = BuildWorkerServiceProvider();
    await MigrateAsync(sp);

    using var scope = sp.CreateScope();
    var enq = scope.ServiceProvider.GetRequiredService<CronSweepEnqueuer>();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    await ClearJobsAsync(db);

    var now = DateTimeOffset.UtcNow;

    await enq.EnqueueDueSweepsAsync(now);
    await enq.EnqueueDueSweepsAsync(now); // same bucket

    var bucketIso = TruncToMinute(now).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:00'Z'");

    var sanityCount = await CountSweepAsync(db, CronSweepEnqueuer.JobSanitySweepType, bucketIso);
    var retryCount = await CountSweepAsync(db, CronSweepEnqueuer.JobRetrySweepType, bucketIso);

    sanityCount.Should().Be(1);
    retryCount.Should().Be(1);
  }

  [Fact]
  public async Task Scheduled_job_executes_when_due()
  {
    using var sp = BuildWorkerServiceProvider();
    await MigrateAsync(sp);

    var now = DateTimeOffset.UtcNow;
    Guid jobId;

    using (var seed = sp.CreateScope())
    {
      var db = seed.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await ClearJobsAsync(db);

      var job = new BackgroundJob
      {
        Id = Guid.NewGuid(),
        Type = "NOOP",
        Status = JobStatuses.Pending,
        Attempts = 0,
        MaxAttempts = 8,
        RunAt = now.AddSeconds(10), // not due yet
        CreatedAt = now,
        UpdatedAt = now
      };

      jobId = job.Id;
      db.Jobs.Add(job);
      await db.SaveChangesAsync();
    }

    // Not due: should not be claimed
    using (var scope = sp.CreateScope())
    {
      var claimer = scope.ServiceProvider.GetRequiredService<JobClaimingService>();
      var claimed = await claimer.ClaimDueAsync("w1", 5, TimeSpan.FromMinutes(5), now, default);
      claimed.Should().BeEmpty();
    }

    // Due: claim + execute + mark succeeded (simulate Worker behavior)
    var dueNow = now.AddSeconds(11);

    using (var scope = sp.CreateScope())
    {
      var claimer = scope.ServiceProvider.GetRequiredService<JobClaimingService>();
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var noop = scope.ServiceProvider.GetRequiredService<NoopJobHandler>();

      var registry = new JobHandlerRegistry();
      registry.Register(noop);

      var claimed = await claimer.ClaimDueAsync("w1", 5, TimeSpan.FromMinutes(5), dueNow, default);
      claimed.Select(x => x.Id).Should().Contain(jobId);

      var job = await db.Jobs.SingleAsync(x => x.Id == jobId);

      registry.TryGet(job.Type, out var handler).Should().BeTrue();
      await handler!.ExecuteAsync(job.Id, job.PayloadJson, default);

      job.Status = JobStatuses.Succeeded;
      job.CompletedAt = DateTimeOffset.UtcNow;
      job.LockedAt = null;
      job.LockedBy = null;
      job.UpdatedAt = DateTimeOffset.UtcNow;

      await db.SaveChangesAsync();
    }

    using (var verify = sp.CreateScope())
    {
      var db = verify.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var refreshed = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);

      refreshed.Status.Should().Be(JobStatuses.Succeeded);
      refreshed.CompletedAt.Should().NotBeNull();
    }
  }

  [Fact]
  public async Task Sanity_sweep_reclaims_stale_running_jobs()
  {
    using var sp = BuildWorkerServiceProvider();
    await MigrateAsync(sp);

    var now = DateTimeOffset.UtcNow;
    Guid jobId;

    using (var seed = sp.CreateScope())
    {
      var db = seed.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await ClearJobsAsync(db);

      var job = new BackgroundJob
      {
        Id = Guid.NewGuid(),
        Type = "NOOP",
        Status = JobStatuses.Running,
        Attempts = 0,
        MaxAttempts = 8,
        RunAt = now.AddSeconds(-1),
        LockedBy = "crashed-worker",
        LockedAt = now.AddMinutes(-10), // stale
        CreatedAt = now,
        UpdatedAt = now
      };

      jobId = job.Id;
      db.Jobs.Add(job);
      await db.SaveChangesAsync();
    }

    using (var scope = sp.CreateScope())
    {
      var handler = scope.ServiceProvider.GetRequiredService<JobSanitySweepHandler>();
      await handler.ExecuteAsync(Guid.NewGuid(), null, default);
    }

    using (var verify = sp.CreateScope())
    {
      var db = verify.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var refreshed = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);

      refreshed.Status.Should().BeOneOf(JobStatuses.Failed, JobStatuses.DeadLetter);
      refreshed.LockedAt.Should().BeNull();
      refreshed.LockedBy.Should().BeNull();
      refreshed.LastError.Should().Be("STALE_LOCK_TIMEOUT");
      refreshed.Attempts.Should().Be(1);
    }
  }

  private ServiceProvider BuildWorkerServiceProvider()
  {
    var sc = new ServiceCollection();

    sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));

    var cs =
      $"Host={_pg.Host};Port={_pg.Port};Database={_pg.Database};Username={_pg.Username};Password={_pg.Password};Include Error Detail=true";

    sc.AddPooledDbContextFactory<MineralKingdomDbContext>(o => o.UseNpgsql(cs));
    sc.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MineralKingdomDbContext>>().CreateDbContext());

    sc.AddScoped<JobClaimingService>();
    sc.AddScoped<NoopJobHandler>();
    sc.AddScoped<JobSanitySweepHandler>();
    sc.AddScoped<JobRetrySweepHandler>();

    sc.AddSingleton<CronSweepEnqueuer>();

    return sc.BuildServiceProvider();
  }

  private static DateTimeOffset TruncToMinute(DateTimeOffset now)
  {
    var unix = now.ToUnixTimeSeconds();
    var bucket = (unix / 60) * 60;
    return DateTimeOffset.FromUnixTimeSeconds(bucket);
  }

  private static async Task MigrateAsync(ServiceProvider sp)
  {
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<int> CountSweepAsync(
    MineralKingdomDbContext db,
    string jobType,
    string bucketIso)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
      await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      SELECT COUNT(*)
      FROM jobs
      WHERE "Type" = @t
        AND "PayloadJson" IS NOT NULL
        AND "PayloadJson"->>'bucket' = @b
        AND "CompletedAt" IS NULL;
      """;

    var p1 = cmd.CreateParameter();
    p1.ParameterName = "t";
    p1.Value = jobType;
    cmd.Parameters.Add(p1);

    var p2 = cmd.CreateParameter();
    p2.ParameterName = "b";
    p2.Value = bucketIso;
    cmd.Parameters.Add(p2);

    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(result);
  }

  private static async Task ClearJobsAsync(MineralKingdomDbContext db)
  {
    await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE jobs;""");
  }
}
