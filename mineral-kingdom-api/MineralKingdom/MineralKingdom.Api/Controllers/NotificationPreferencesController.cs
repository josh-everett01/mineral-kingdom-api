using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Notifications;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/me/notification-preferences")]
[Authorize(Policy = AuthorizationPolicies.EmailVerified)]
public sealed class NotificationPreferencesController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly UserNotificationPreferencesService _prefs;

  public NotificationPreferencesController(MineralKingdomDbContext db, UserNotificationPreferencesService prefs)
  {
    _db = db;
    _prefs = prefs;
  }

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var prefs = await _prefs.GetOrCreateAsync(userId, now, ct);

    return Ok(new
    {
      prefs.OutbidEmailEnabled,
      prefs.BidAcceptedEmailEnabled,
      prefs.AuctionPaymentRemindersEnabled,
      prefs.ShippingInvoiceRemindersEnabled,
      prefs.UpdatedAt
    });
  }

  [HttpPut]
  public async Task<IActionResult> Update([FromBody] UpdateNotificationPreferencesRequest req, CancellationToken ct)
  {
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var prefs = await _db.UserNotificationPreferences.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    if (prefs is null)
    {
      prefs = new Infrastructure.Persistence.Entities.UserNotificationPreferences
      {
        UserId = userId,
        UpdatedAt = now
      };
      _db.UserNotificationPreferences.Add(prefs);
    }

    if (req.OutbidEmailEnabled.HasValue) prefs.OutbidEmailEnabled = req.OutbidEmailEnabled.Value;
    if (req.BidAcceptedEmailEnabled.HasValue) prefs.BidAcceptedEmailEnabled = req.BidAcceptedEmailEnabled.Value;
    if (req.AuctionPaymentRemindersEnabled.HasValue) prefs.AuctionPaymentRemindersEnabled = req.AuctionPaymentRemindersEnabled.Value;
    if (req.ShippingInvoiceRemindersEnabled.HasValue) prefs.ShippingInvoiceRemindersEnabled = req.ShippingInvoiceRemindersEnabled.Value;

    prefs.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }
}