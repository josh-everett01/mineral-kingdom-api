using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security.Jobs;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class JobRetryDlqTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public JobRetryDlqTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public void Retry_policy_matches_design_schedule_and_moves_to_dlq()
  {
    var now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    var job = new BackgroundJob
    {
      Id = Guid.NewGuid(),
      Type = "TEST",
      Status = JobStatuses.Running,
      Attempts = 0,
      MaxAttempts = RetryPolicy.DefaultMaxAttempts,
      RunAt = now,
      CreatedAt = now,
      UpdatedAt = now
    };

    // attempt -> expected delay
    var expected = new Dictionary<int, TimeSpan>
    {
      [1] = TimeSpan.Zero,
      [2] = TimeSpan.FromMinutes(1),
      [3] = TimeSpan.FromMinutes(5),
      [4] = TimeSpan.FromMinutes(15),
      [5] = TimeSpan.FromHours(1),
      [6] = TimeSpan.FromHours(3),
      [7] = TimeSpan.FromHours(12),
      [8] = TimeSpan.FromHours(24)
    };

    // Fail 7 times => FAILED with scheduled RunAt
    for (var i = 1; i <= 7; i++)
    {
      JobFailureProcessor.ApplyFailure(job, now, "BOOM", includeJitter: false);

      job.Attempts.Should().Be(i);
      job.Status.Should().Be(JobStatuses.Failed);
      job.RunAt.Should().Be(now + expected[i]);
      job.LastError.Should().Be("BOOM");
    }

    // 8th failure => DLQ
    JobFailureProcessor.ApplyFailure(job, now, "BOOM", includeJitter: false);
    job.Attempts.Should().Be(8);
    job.Status.Should().Be(JobStatuses.DeadLetter);
  }

  [Fact]
  public async Task ClaimDue_claims_failed_jobs_when_they_become_due()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid jobId;

    // Seed a FAILED job that is due
    using (var seedScope = factory.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var job = new BackgroundJob
      {
        Id = Guid.NewGuid(),
        Type = "NOOP",
        Status = JobStatuses.Failed,
        Attempts = 1,
        MaxAttempts = 8,
        RunAt = now.AddSeconds(-1),
        CreatedAt = now,
        UpdatedAt = now,
        LastError = "FAIL"
      };

      jobId = job.Id;
      db.Jobs.Add(job);
      await db.SaveChangesAsync();
    }

    using var scope = factory.Services.CreateScope();
    var claimer = scope.ServiceProvider.GetRequiredService<JobClaimingService>();

    var claimed = await claimer.ClaimDueAsync("w1", batchSize: 5, lockTimeout: TimeSpan.FromMinutes(5), now: now, ct: default);
    claimed.Select(x => x.Id).Should().Contain(jobId);

    using var verifyScope = factory.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var refreshed = await verifyDb.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
    refreshed.Status.Should().Be(JobStatuses.Running);
    refreshed.LockedBy.Should().Be("w1");
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  [Fact]
  public async Task Worker_retries_then_moves_job_to_dlq_after_max_attempts()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid jobId;

    // Seed a job that will always fail
    using (var seedScope = factory.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var job = new BackgroundJob
      {
        Id = Guid.NewGuid(),
        Type = "ALWAYS_FAIL",
        Status = JobStatuses.Pending,
        Attempts = 0,
        MaxAttempts = RetryPolicy.DefaultMaxAttempts,
        RunAt = now.AddSeconds(-1),
        CreatedAt = now,
        UpdatedAt = now,
        PayloadJson = "{}"
      };

      jobId = job.Id;
      db.Jobs.Add(job);
      await db.SaveChangesAsync();
    }

    // Simulate the worker running repeatedly.
    // We do NOT want to actually wait real time for backoffs, so we "force due" between passes.
    for (var i = 1; i <= RetryPolicy.DefaultMaxAttempts; i++)
    {
      using var scope = factory.Services.CreateScope();
      var claimer = scope.ServiceProvider.GetRequiredService<JobClaimingService>();
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Claim it (due)
      var claimed = await claimer.ClaimDueAsync("test-worker", 1, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, default);
      claimed.Should().NotBeEmpty();

      // Execute using the same failure processor semantics as the worker would
      var job = await db.Jobs.SingleAsync(x => x.Id == jobId);

      // force the same behavior as Worker.ExecuteOneAsync catch block
      JobFailureProcessor.ApplyFailure(job, DateTimeOffset.UtcNow, "FORCED_FAILURE", includeJitter: false);
      await db.SaveChangesAsync();

      // Force due again for next loop (avoid sleeping)
      if (job.Status != JobStatuses.DeadLetter)
      {
        job.RunAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
      }
    }

    // Final assert: it should be DLQ
    using (var verifyScope = factory.Services.CreateScope())
    {
      var db = verifyScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var final = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);

      final.Status.Should().Be(JobStatuses.DeadLetter);
      final.Attempts.Should().Be(RetryPolicy.DefaultMaxAttempts);
      final.LastError.Should().NotBeNull();
    }
  }

}
