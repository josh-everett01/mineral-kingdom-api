using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Security;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class JobQueueFoundationTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public JobQueueFoundationTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Can_enqueue_job()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var scope = factory.Services.CreateScope();
    var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var id = await queue.EnqueueAsync("TEST_JOB", new { hello = "world" });

    var job = await db.Jobs.FindAsync(id);
    job.Should().NotBeNull();
    job!.Status.Should().Be(JobStatuses.Pending);
    job.Attempts.Should().Be(0);
    job.MaxAttempts.Should().Be(8);
    job.PayloadJson.Should().NotBeNull();
  }

  [Fact]
  public async Task Can_mark_job_succeeded()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var scope = factory.Services.CreateScope();
    var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var id = await queue.EnqueueAsync("TEST_JOB", new { a = 1 });

    var ok = await queue.MarkSucceededAsync(id);
    ok.Should().BeTrue();

    var job = await db.Jobs.FindAsync(id);
    job.Should().NotBeNull();
    job!.Status.Should().Be(JobStatuses.Succeeded);
    job.CompletedAt.Should().NotBeNull();
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.EnsureCreatedAsync();
  }
}
