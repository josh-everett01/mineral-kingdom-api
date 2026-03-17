namespace MineralKingdom.Infrastructure.Configuration;

public sealed class CheckoutOptions
{
  public int HoldInitialMinutes { get; set; } = 10;
  public int HoldMaxMinutes { get; set; } = 30;
  public int HoldExtendThresholdSeconds { get; set; } = 60;
  public int HoldMaxExtensions { get; set; } = 2;
}