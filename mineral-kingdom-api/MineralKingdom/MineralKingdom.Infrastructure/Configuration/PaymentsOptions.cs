namespace MineralKingdom.Infrastructure.Configuration;

public sealed class PaymentsOptions
{
  /// <summary>
  /// When set to "FAKE", the API will use the Fake provider even if the request says STRIPE/PAYPAL.
  /// Useful for tests and local dev without external credentials.
  /// </summary>
  public string? Mode { get; set; }
}
