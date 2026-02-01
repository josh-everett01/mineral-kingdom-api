using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class DbJobQueue : IJobQueue
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly MineralKingdomDbContext _db;

  public DbJobQueue(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public async Task<Guid> EnqueueAsync(
    string type,
    object? payload,
    DateTimeOffset? runAt = null,
    int? maxAttempts = null,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(type))
      throw new ArgumentException("Job type is required.", nameof(type));

    var now = DateTimeOffset.UtcNow;
    var job = new BackgroundJob
    {
      Id = Guid.NewGuid(),
      Type = type.Trim().ToUpperInvariant(),
      Status = JobStatuses.Pending,
      PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions),
      Attempts = 0,
      MaxAttempts = maxAttempts ?? 8,
      RunAt = runAt ?? now,
      CreatedAt = now,
      UpdatedAt = now
    };

    if (job.MaxAttempts < 1)
      throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be >= 1.");

    _db.Jobs.Add(job);
    await _db.SaveChangesAsync(ct);

    return job.Id;
  }

  public async Task<bool> MarkSucceededAsync(Guid jobId, CancellationToken ct = default)
  {
    var job = await _db.Jobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
    if (job is null) return false;

    var now = DateTimeOffset.UtcNow;
    job.Status = JobStatuses.Succeeded;
    job.CompletedAt = now;
    job.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return true;
  }
}
