using System;

namespace MineralKingdom.Infrastructure.Security.Jobs;

/// <summary>
/// Shared retry + backoff policy for background jobs.
///
/// Design doc schedule (max_attempts: 8):
/// 1) immediate
/// 2) +1 min
/// 3) +5 min
/// 4) +15 min
/// 5) +1 hour
/// 6) +3 hours
/// 7) +12 hours
/// 8) +24 hours
/// </summary>
public static class RetryPolicy
{
  public const int DefaultMaxAttempts = 8;

  /// <summary>
  /// Returns the base delay (no jitter) for a given retry attempt number (1-based).
  /// </summary>
  public static TimeSpan GetBaseDelay(int attemptNumber)
  {
    return attemptNumber switch
    {
      1 => TimeSpan.Zero,
      2 => TimeSpan.FromMinutes(1),
      3 => TimeSpan.FromMinutes(5),
      4 => TimeSpan.FromMinutes(15),
      5 => TimeSpan.FromHours(1),
      6 => TimeSpan.FromHours(3),
      7 => TimeSpan.FromHours(12),
      8 => TimeSpan.FromHours(24),
      _ => TimeSpan.FromHours(24)
    };
  }

  /// <summary>
  /// Computes the next RunAt timestamp for the given retry attempt.
  ///
  /// Jitter: +/-10% of base delay, capped to +/-2 minutes, never negative.
  /// Disable jitter in tests by setting includeJitter=false.
  /// </summary>
  public static DateTimeOffset ComputeNextRunAt(
    DateTimeOffset now,
    int attemptNumber,
    bool includeJitter = true,
    Random? random = null)
  {
    var baseDelay = GetBaseDelay(attemptNumber);

    if (!includeJitter || baseDelay == TimeSpan.Zero)
      return now + baseDelay;

    random ??= Random.Shared;
    var jittered = ApplyJitter(baseDelay, random);
    return now + jittered;
  }

  private static TimeSpan ApplyJitter(TimeSpan baseDelay, Random random)
  {
    // +/-10% jitter, capped to +/-2 minutes.
    var maxJitterSeconds = Math.Min(baseDelay.TotalSeconds * 0.10, TimeSpan.FromMinutes(2).TotalSeconds);

    // random.NextDouble() => [0,1); convert to [-1,1)
    var signScaled = (random.NextDouble() * 2.0) - 1.0;
    var jitterSeconds = signScaled * maxJitterSeconds;

    var totalSeconds = baseDelay.TotalSeconds + jitterSeconds;
    if (totalSeconds < 0) totalSeconds = 0;

    // Avoid sub-millisecond noise in DB timestamps.
    return TimeSpan.FromSeconds(Math.Round(totalSeconds));
  }
}
