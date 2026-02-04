namespace MineralKingdom.Contracts.Store;

public static class DiscountTypes
{
  public const string None = "NONE";
  public const string Flat = "FLAT";
  public const string Percent = "PERCENT";

  public static bool IsValid(string? discountType)
    => discountType is None or Flat or Percent;
}
