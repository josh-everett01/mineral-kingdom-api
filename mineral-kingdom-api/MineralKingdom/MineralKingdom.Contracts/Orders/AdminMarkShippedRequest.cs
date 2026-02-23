namespace MineralKingdom.Contracts.Orders;

public sealed class AdminMarkShippedRequest
{
  public string ShippingCarrier { get; init; } = string.Empty;
  public string TrackingNumber { get; init; } = string.Empty;
}