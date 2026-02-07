namespace MineralKingdom.Infrastructure.Configuration;

public sealed class CheckoutOptions
{
  public int HoldInitialMinutes { get; init; } = 10;
  public int HoldMaxMinutes { get; init; } = 30;
}
