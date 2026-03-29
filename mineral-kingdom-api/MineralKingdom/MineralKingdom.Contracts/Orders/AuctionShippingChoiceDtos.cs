namespace MineralKingdom.Contracts.Orders;

public static class AuctionShippingModes
{
  public const string Unselected = "UNSELECTED";
  public const string ShipNow = "SHIP_NOW";
  public const string OpenBox = "OPEN_BOX";
}

public sealed record SetAuctionShippingChoiceRequest(
  string ShippingMode
);

public sealed record AuctionShippingChoiceResponse(
  Guid OrderId,
  string ShippingMode,
  int SubtotalCents,
  int DiscountTotalCents,
  int ShippingAmountCents,
  int TotalCents,
  string CurrencyCode,
  Guid? AuctionId,
  int? QuotedShippingCents
);