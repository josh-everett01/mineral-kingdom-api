namespace MineralKingdom.Infrastructure.Notifications;

public static class EmailDedupeKeys
{
  private static string Norm(string email) => email.Trim().ToLowerInvariant();

  public static string WinningBid(Guid auctionId, string toEmail) =>
    $"{EmailTemplateKeys.WinningBid}|AUCTION:{auctionId}|TO:{Norm(toEmail)}";

  public static string PaymentReceived(Guid orderId, string toEmail) =>
    $"{EmailTemplateKeys.PaymentReceived}|ORDER:{orderId}|TO:{Norm(toEmail)}";

  public static string ShippingInvoiceCreated(Guid invoiceId, string toEmail) =>
    $"{EmailTemplateKeys.ShippingInvoiceCreated}|INVOICE:{invoiceId}|TO:{Norm(toEmail)}";

  public static string Outbid(Guid auctionId, Guid outbidUserId, int newCurrentPriceCents, string toEmail) =>
$"{EmailTemplateKeys.Outbid}|AUCTION:{auctionId}|OUTBID:{outbidUserId}|PRICE:{newCurrentPriceCents}|TO:{Norm(toEmail)}";

  public static string ShippingInvoicePaid(Guid invoiceId, string toEmail) =>
    $"{EmailTemplateKeys.ShippingInvoicePaid}|INVOICE:{invoiceId}|TO:{Norm(toEmail)}";

  public static string ShipmentConfirmed(Guid groupId, string toEmail) =>
    $"{EmailTemplateKeys.ShipmentConfirmed}|GROUP:{groupId}|TO:{Norm(toEmail)}";
}
