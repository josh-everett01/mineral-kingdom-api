using Microsoft.Extensions.Configuration;

namespace MineralKingdom.Worker.Cron;

public sealed record CronSweepSettings(
  bool Enabled,
  int TickSeconds)
{
  public static CronSweepSettings FromConfiguration(IConfiguration cfg)
  {
    // Prefer MK_WORKER__CRON_SWEEPS__* env vars, but allow a simpler CronSweeps__* too.
    bool enabled =
      cfg.GetValue<bool?>("MK_WORKER:CRON_SWEEPS:ENABLED")
      ?? cfg.GetValue<bool?>("CronSweeps:Enabled")
      ?? true;

    int tickSeconds =
      cfg.GetValue<int?>("MK_WORKER:CRON_SWEEPS:TICK_SECONDS")
      ?? cfg.GetValue<int?>("CronSweeps:TickSeconds")
      ?? 30;

    if (tickSeconds < 1) tickSeconds = 1;

    return new CronSweepSettings(enabled, tickSeconds);
  }
}
