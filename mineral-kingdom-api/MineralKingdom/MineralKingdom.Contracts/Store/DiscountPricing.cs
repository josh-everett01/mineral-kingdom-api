namespace MineralKingdom.Contracts.Store;

public static class DiscountPricing
{
  // 10,000 bps = 100%
  public const int MaxBps = 10_000;

  public static (bool Ok, string? Error) Validate(
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps)
  {
    if (priceCents <= 0) return (false, "PRICE_CENTS_INVALID");

    var dt = (discountType ?? "").Trim().ToUpperInvariant();
    if (!DiscountTypes.IsValid(dt)) return (false, "DISCOUNT_TYPE_INVALID");

    if (dt == DiscountTypes.None)
    {
      if (discountCents is not null) return (false, "DISCOUNT_CENTS_NOT_ALLOWED_FOR_NONE");
      if (discountPercentBps is not null) return (false, "DISCOUNT_PERCENT_NOT_ALLOWED_FOR_NONE");
      return (true, null);
    }

    if (dt == DiscountTypes.Flat)
    {
      if (discountCents is null) return (false, "DISCOUNT_CENTS_REQUIRED");
      if (discountCents <= 0) return (false, "DISCOUNT_CENTS_INVALID");
      if (discountCents >= priceCents) return (false, "DISCOUNT_CENTS_TOO_LARGE");
      if (discountPercentBps is not null) return (false, "DISCOUNT_PERCENT_NOT_ALLOWED_FOR_FLAT");
      return (true, null);
    }

    if (dt == DiscountTypes.Percent)
    {
      if (discountPercentBps is null) return (false, "DISCOUNT_PERCENT_REQUIRED");
      if (discountPercentBps <= 0 || discountPercentBps > MaxBps) return (false, "DISCOUNT_PERCENT_INVALID");
      if (discountCents is not null) return (false, "DISCOUNT_CENTS_NOT_ALLOWED_FOR_PERCENT");
      return (true, null);
    }

    return (false, "DISCOUNT_TYPE_INVALID");
  }

  public static int ComputeEffectivePriceCents(
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps)
  {
    var dt = (discountType ?? "").Trim().ToUpperInvariant();
    if (dt == DiscountTypes.None) return priceCents;

    if (dt == DiscountTypes.Flat)
    {
      var off = discountCents ?? 0;
      var v = priceCents - off;
      return v < 0 ? 0 : v;
    }

    if (dt == DiscountTypes.Percent)
    {
      var bps = discountPercentBps ?? 0;
      // round to nearest cent: (price * bps + 5000) / 10000
      var discount = (int)((priceCents * (long)bps + 5_000L) / MaxBps);
      var v = priceCents - discount;
      return v < 0 ? 0 : v;
    }

    return priceCents;
  }
}
