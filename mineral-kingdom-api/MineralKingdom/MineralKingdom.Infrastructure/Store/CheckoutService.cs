using Microsoft.EntityFrameworkCore;
using Npgsql;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Store;

public sealed class CheckoutService
{
  private readonly MineralKingdomDbContext _db;

  public CheckoutService(MineralKingdomDbContext db) => _db = db;

  public async Task<(bool Ok, string? Error, CheckoutHold? Hold)> StartCheckoutAsync(
    Cart cart,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE", null);
    if (cart.Lines.Count == 0) return (false, "CART_EMPTY", null);

    // Expire any holds that are past expiry
    var activeHold = await _db.CheckoutHolds
      .SingleOrDefaultAsync(h => h.CartId == cart.Id && h.Status == CheckoutHoldStatuses.Active, ct);

    if (activeHold is not null)
    {
      if (now <= activeHold.ExpiresAt)
        return (true, null, activeHold);

      activeHold.Status = CheckoutHoldStatuses.Expired;
      activeHold.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
    }

    var hold = new CheckoutHold
    {
      Id = Guid.NewGuid(),
      CartId = cart.Id,
      UserId = userId,
      Status = CheckoutHoldStatuses.Active,
      ExpiresAt = now.AddMinutes(20),
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.CheckoutHolds.Add(hold);
    await _db.SaveChangesAsync(ct);

    return (true, null, hold);
  }

  public async Task<(bool Ok, string? Error)> CompletePaymentAsync(
    Guid holdId,
    Guid? actingUserId,
    string paymentReference,
    DateTimeOffset now,
    CancellationToken ct)
  {
    // Backward-compatible shim:
    // keep the method name but make it safe (S4-3)
    return await RecordClientReturnAsync(holdId, actingUserId, paymentReference, now, ct);
  }

  public async Task<(bool Ok, string? Error)> RecordClientReturnAsync(
  Guid holdId,
  Guid? actingUserId,
  string paymentReference,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND");

    // If this is a member hold, ensure the actor matches
    if (hold.UserId.HasValue && actingUserId != hold.UserId)
      return (false, "FORBIDDEN");

    hold.ClientReturnedAt = now;
    hold.ClientReturnReference = paymentReference;
    hold.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> ConfirmPaidFromWebhookAsync(
  Guid holdId,
  string paymentReference,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND");

    if (hold.Status != CheckoutHoldStatuses.Active)
      return (false, "HOLD_NOT_ACTIVE");

    if (now > hold.ExpiresAt)
    {
      hold.Status = CheckoutHoldStatuses.Expired;
      hold.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
      return (false, "HOLD_EXPIRED");
    }

    // Winner selection (existing approach stays the same)
    hold.CompletedAt = now;
    hold.PaymentReference = paymentReference;
    hold.Status = CheckoutHoldStatuses.Completed;
    hold.UpdatedAt = now;

    var cart = await _db.Carts.SingleAsync(c => c.Id == hold.CartId, ct);
    cart.Status = CartStatuses.CheckedOut;
    cart.UpdatedAt = now;

    try
    {
      await _db.SaveChangesAsync(ct);
      return (true, null);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
    {
      return (false, "PAYMENT_ALREADY_COMPLETED");
    }
  }
}
