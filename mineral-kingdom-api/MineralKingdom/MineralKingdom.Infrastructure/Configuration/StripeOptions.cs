namespace MineralKingdom.Infrastructure.Configuration;

public sealed class StripeOptions
{
  public string? SecretKey { get; set; }
  public string? WebhookSecret { get; set; }
}
