namespace MineralKingdom.Infrastructure.Configuration;

public sealed class PayPalOptions
{
  // Required for real mode
  public string? ClientId { get; set; }
  public string? Secret { get; set; }
  public string? WebhookId { get; init; }


  // "Sandbox" (default) or "Live"
  public string Environment { get; set; } = "Sandbox";
}
