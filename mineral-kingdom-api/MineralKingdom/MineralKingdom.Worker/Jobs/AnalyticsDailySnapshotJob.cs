using System.Text.Json;
using Microsoft.Extensions.Logging;
using MineralKingdom.Infrastructure.Analytics;

namespace MineralKingdom.Worker.Jobs;

public sealed class AnalyticsDailySnapshotJob : IJobHandler
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly AnalyticsSnapshotService _snapshots;
  private readonly ILogger<AnalyticsDailySnapshotJob> _logger;

  public AnalyticsDailySnapshotJob(AnalyticsSnapshotService snapshots, ILogger<AnalyticsDailySnapshotJob> logger)
  {
    _snapshots = snapshots;
    _logger = logger;
  }

  public string Type => JobTypes.AnalyticsDailySnapshot;

  public async Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    // payload created by CronSweepEnqueuer includes "bucket"
    var bucket = TryGetBucket(payloadJson) ?? now.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd");

    if (!DateTime.TryParse(bucket, out var date))
      date = now.UtcDateTime.Date.AddDays(-1);

    await _snapshots.GenerateDailyAsync(date.Date, now, ct);

    _logger.LogInformation("Analytics snapshot complete. Date={Date} JobId={JobId}", date.Date, jobId);
  }

  private static string? TryGetBucket(string? payloadJson)
  {
    if (string.IsNullOrWhiteSpace(payloadJson)) return null;
    try
    {
      using var doc = JsonDocument.Parse(payloadJson);
      if (doc.RootElement.TryGetProperty("bucket", out var b) && b.ValueKind == JsonValueKind.String)
        return b.GetString();
    }
    catch { }
    return null;
  }
}