namespace MineralKingdom.Contracts.Orders;

public sealed class AdminCreateShippingInvoiceRequest
{
  public long? AmountCents { get; set; }
  public string? Reason { get; set; }
}