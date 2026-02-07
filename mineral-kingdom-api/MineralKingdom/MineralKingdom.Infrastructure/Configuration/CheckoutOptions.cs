namespace MineralKingdom.Infrastructure.Configuration;

public sealed class CheckoutOptions
{
  public int HoldInitialMinutes { get; init; } = 5;
  public int HoldMaxMinutes { get; init; } = 20;
}
