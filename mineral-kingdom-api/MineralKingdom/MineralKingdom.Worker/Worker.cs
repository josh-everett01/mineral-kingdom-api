using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security.Jobs;
using MineralKingdom.Worker.Jobs;
using System.Diagnostics;

namespace MineralKingdom.Worker;

public sealed class Worker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<Worker> _logger;

    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(5);
    private const int BatchSize = 10;

    public Worker(IServiceProvider services, ILogger<Worker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MineralKingdom.Worker started. WorkerId={WorkerId}", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WorkOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker loop error");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task WorkOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
        var claimer = scope.ServiceProvider.GetRequiredService<JobClaimingService>();

        // Register handlers (keep it simple for now)
        var registry = new JobHandlerRegistry();
        registry.Register(scope.ServiceProvider.GetRequiredService<NoopJobHandler>());

        var now = DateTimeOffset.UtcNow;
        var claimed = await claimer.ClaimDueAsync(_workerId, BatchSize, _lockTimeout, now, ct);

        if (claimed.Count == 0)
            return;

        foreach (var job in claimed)
        {
            await ExecuteOneAsync(db, registry, job.Id, ct);
        }
    }

    private async Task ExecuteOneAsync(
      MineralKingdomDbContext db,
      JobHandlerRegistry registry,
      Guid jobId,
      CancellationToken ct)
    {
        var job = await db.Jobs.SingleAsync(x => x.Id == jobId, ct);

        // Safety: only execute if we are the locker + job is RUNNING
        if (!string.Equals(job.LockedBy, _workerId, StringComparison.Ordinal) ||
            !string.Equals(job.Status, JobStatuses.Running, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Skipping job not owned by this worker. JobId={JobId}", jobId);
            return;
        }

        if (!registry.TryGet(job.Type, out var handler))
        {
            await FailAsync(db, job, "NO_HANDLER_FOR_TYPE", deadLetter: true, ct);
            return;
        }

        try
        {
            // Idempotency rule:
            // - handlers must be safe to run twice
            // - use job.Id as idempotency key when writing to DB (future job types)
            await handler.ExecuteAsync(job.Id, job.PayloadJson, ct);

            job.Status = JobStatuses.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.LastError = null;
            job.LockedAt = null;
            job.LockedBy = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Retry / DLQ behavior
            job.Attempts += 1;
            job.LastError = ex.Message;
            job.LockedAt = null;
            job.LockedBy = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            if (job.Attempts >= job.MaxAttempts)
            {
                job.Status = JobStatuses.DeadLetter;
            }
            else
            {
                job.Status = JobStatuses.Pending;
                job.RunAt = DateTimeOffset.UtcNow.AddSeconds(10 * job.Attempts); // simple backoff
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task FailAsync(
      MineralKingdomDbContext db,
      BackgroundJob job,
      string error,
      bool deadLetter,
      CancellationToken ct)
    {
        job.Attempts += 1;
        job.LastError = error;
        job.LockedAt = null;
        job.LockedBy = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        job.Status = deadLetter ? JobStatuses.DeadLetter : JobStatuses.Failed;
        await db.SaveChangesAsync(ct);
    }
}