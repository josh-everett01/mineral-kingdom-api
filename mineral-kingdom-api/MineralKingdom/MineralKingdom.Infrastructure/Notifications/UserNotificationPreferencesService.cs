using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Notifications;

public sealed class UserNotificationPreferencesService
{
  private readonly MineralKingdomDbContext _db;

  public UserNotificationPreferencesService(MineralKingdomDbContext db) => _db = db;

  public async Task<UserNotificationPreferences> GetOrCreateAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
  {
    var prefs = await _db.UserNotificationPreferences
      .SingleOrDefaultAsync(x => x.UserId == userId, ct);

    if (prefs is not null) return prefs;

    prefs = new UserNotificationPreferences
    {
      UserId = userId,
      // defaults handled by entity defaults / migration defaults
      UpdatedAt = now
    };

    _db.UserNotificationPreferences.Add(prefs);
    await _db.SaveChangesAsync(ct);
    return prefs;
  }

  public static bool IsEnabled(UserNotificationPreferences prefs, string optionalKey)
  {
    // Map optional notification keys to preference flags
    return optionalKey switch
    {
      OptionalEmailKeys.Outbid => prefs.OutbidEmailEnabled,
      OptionalEmailKeys.BidAccepted => prefs.BidAcceptedEmailEnabled,
      OptionalEmailKeys.AuctionPaymentReminders => prefs.AuctionPaymentRemindersEnabled,
      OptionalEmailKeys.ShippingInvoiceReminders => prefs.ShippingInvoiceRemindersEnabled,
      _ => true
    };
  }
}

public static class OptionalEmailKeys
{
  public const string Outbid = "OUTBID";
  public const string BidAccepted = "BID_ACCEPTED";
  public const string AuctionPaymentReminders = "AUCTION_PAYMENT_REMINDERS";
  public const string ShippingInvoiceReminders = "SHIPPING_INVOICE_REMINDERS";
}