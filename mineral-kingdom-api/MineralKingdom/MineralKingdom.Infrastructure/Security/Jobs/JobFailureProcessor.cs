using System;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security.Jobs;

/// <summary>
/// Centralizes how job failures are recorded, retried, and moved to DLQ.
/// Keeps the worker loop small and ensures policy is testable.
/// </summary>
public static class JobFailureProcessor
{
  private const int MaxErrorLength = 1024;

  /// <summary>
  /// Applies retry/DLQ rules after a failure.
  ///
  /// Notes:
  /// - Increments Attempts (1 failure == Attempts=1).
  /// - If Attempts >= MaxAttempts => DEAD_LETTER.
  /// - Otherwise => FAILED + RunAt scheduled via <see cref="RetryPolicy"/>.
  /// </summary>
  public static void ApplyFailure(
    BackgroundJob job,
    DateTimeOffset now,
    string error,
    bool includeJitter = true,
    Random? random = null)
  {
    if (job is null) throw new ArgumentNullException(nameof(job));

    job.Attempts += 1;
    job.LastError = Truncate(error);

    // Always release the lock on failure so another worker can pick it up later.
    job.LockedAt = null;
    job.LockedBy = null;
    job.UpdatedAt = now;

    if (job.Attempts >= job.MaxAttempts)
    {
      job.Status = JobStatuses.DeadLetter;
      return;
    }

    job.Status = JobStatuses.Failed;
    job.RunAt = RetryPolicy.ComputeNextRunAt(now, job.Attempts, includeJitter, random);
  }

  private static string Truncate(string? s)
  {
    if (string.IsNullOrWhiteSpace(s)) return "UNKNOWN_ERROR";
    s = s.Trim();
    return s.Length <= MaxErrorLength ? s : s[..MaxErrorLength];
  }
}
