namespace MineralKingdom.Contracts.Auctions;

public static class AuctionLaunchModes
{
  public const string Draft = "DRAFT";
  public const string LaunchNow = "NOW";
  public const string Scheduled = "SCHEDULED";

  public static bool IsValid(string? value)
  {
    var normalized = (value ?? "").Trim().ToUpperInvariant();
    return normalized == Draft || normalized == LaunchNow || normalized == Scheduled;
  }
}

public static class AuctionTimingModes
{
  public const string PresetDuration = "PRESET_DURATION";
  public const string Manual = "MANUAL";

  public static bool IsValid(string? value)
  {
    var normalized = (value ?? "").Trim().ToUpperInvariant();
    return normalized == PresetDuration || normalized == Manual;
  }
}