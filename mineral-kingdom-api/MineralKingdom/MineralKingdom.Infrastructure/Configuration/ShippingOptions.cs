namespace MineralKingdom.Infrastructure.Configuration;

public sealed class ShippingOptions
{
  public string CurrencyCode { get; init; } = "USD";
  public List<ShippingTier> Tiers { get; init; } = new();
}

public sealed class ShippingTier
{
  // Inclusive lower bound, inclusive upper bound (in cents of merchandise total)
  public long MinMerchTotalCents { get; init; }
  public long MaxMerchTotalCents { get; init; }
  public long ShippingCents { get; init; }
}