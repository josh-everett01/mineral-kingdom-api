namespace MineralKingdom.Contracts.Notifications;

public sealed record UpdateNotificationPreferencesRequest(
  bool? OutbidEmailEnabled,
  bool? BidAcceptedEmailEnabled,
  bool? AuctionPaymentRemindersEnabled,
  bool? ShippingInvoiceRemindersEnabled);