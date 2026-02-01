using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security.Jobs;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class WorkerExecutionLoopTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public WorkerExecutionLoopTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Two_workers_do_not_claim_the_same_job()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    // Seed one due job
    Guid jobId;
    using (var seedScope = factory.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var job = new BackgroundJob
      {
        Id = Guid.NewGuid(),
        Type = "NOOP",
        Status = JobStatuses.Pending,
        PayloadJson = null,
        Attempts = 0,
        MaxAttempts = 8,
        RunAt = now.AddSeconds(-1),
        CreatedAt = now,
        UpdatedAt = now
      };

      jobId = job.Id;

      db.Jobs.Add(job);
      await db.SaveChangesAsync();
    }

    // Two independent "workers" with independent DI scopes
    using var scope1 = factory.Services.CreateScope();
    using var scope2 = factory.Services.CreateScope();

    var w1 = scope1.ServiceProvider.GetRequiredService<JobClaimingService>();
    var w2 = scope2.ServiceProvider.GetRequiredService<JobClaimingService>();

    var t1 = w1.ClaimDueAsync("w1", batchSize: 5, lockTimeout: TimeSpan.FromMinutes(5), now: now, ct: default);
    var t2 = w2.ClaimDueAsync("w2", batchSize: 5, lockTimeout: TimeSpan.FromMinutes(5), now: now, ct: default);

    await Task.WhenAll(t1, t2);

    var claimed1 = t1.Result.Select(x => x.Id).ToList();
    var claimed2 = t2.Result.Select(x => x.Id).ToList();

    // Exactly one worker should claim the job.
    (claimed1.Count + claimed2.Count).Should().Be(1);

    // Re-load from a fresh scope/DbContext to avoid any tracking artifacts
    using var verifyScope = factory.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var refreshed = await verifyDb.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
    refreshed.Status.Should().Be(JobStatuses.Running);
    new[] { "w1", "w2" }.Should().Contain(refreshed.LockedBy);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}
