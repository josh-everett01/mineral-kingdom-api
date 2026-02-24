namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class UserNotificationPreferences
{
  public Guid UserId { get; set; }

  // Optional toggles (design defaults)
  public bool OutbidEmailEnabled { get; set; } = true;
  public bool AuctionPaymentRemindersEnabled { get; set; } = true;
  public bool ShippingInvoiceRemindersEnabled { get; set; } = true;
  public bool BidAcceptedEmailEnabled { get; set; } = false;

  public DateTimeOffset UpdatedAt { get; set; }
}