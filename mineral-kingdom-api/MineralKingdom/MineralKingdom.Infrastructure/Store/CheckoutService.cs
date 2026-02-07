using Microsoft.EntityFrameworkCore;
using Npgsql;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Store;

public sealed class CheckoutService
{
  private readonly MineralKingdomDbContext _db;
  private readonly CheckoutOptions _opts;

  public CheckoutService(MineralKingdomDbContext db, IOptions<CheckoutOptions> opts)
  {
    _db = db;
    _opts = opts.Value;
  }

  public async Task<(bool Ok, string? Error, CheckoutHold? Hold)> StartCheckoutAsync(
    Cart cart,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE", null);
    if (cart.Lines.Count == 0) return (false, "CART_EMPTY", null);

    // Resolve offer -> listing IDs for cart lines
    var offerIds = cart.Lines.Select(l => l.OfferId).Distinct().ToList();

    var offers = await _db.StoreOffers
      .AsNoTracking()
      .Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null && o.IsActive)
      .Select(o => new { o.Id, o.ListingId })
      .ToListAsync(ct);

    if (offers.Count != offerIds.Count)
      return (false, "OFFER_NOT_FOUND", null);

    var offerToListing = offers.ToDictionary(x => x.Id, x => x.ListingId);

    var holdTargets = cart.Lines
      .Select(l => new { OfferId = l.OfferId, ListingId = offerToListing[l.OfferId] })
      .Distinct()
      .ToList();

    // ✅ Defensive: ensure listing is purchasable (qty=1 world)
    var listingIds = holdTargets.Select(x => x.ListingId).Distinct().ToList();

    var listings = await _db.Listings
      .AsNoTracking()
      .Where(l => listingIds.Contains(l.Id))
      .Select(l => new { l.Id, l.Status, l.QuantityAvailable })
      .ToListAsync(ct);

    if (listings.Count != listingIds.Count)
      return (false, "LISTING_NOT_FOUND", null);

    if (listings.Any(l =>
          string.Equals(l.Status, ListingStatuses.Sold, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(l.Status, ListingStatuses.Archived, StringComparison.OrdinalIgnoreCase)))
      return (false, "LISTING_NOT_FOR_SALE", null);

    if (listings.Any(l => l.QuantityAvailable < 1))
      return (false, "OUT_OF_STOCK", null);


    // Reuse existing active hold for this cart if still valid
    var existing = await _db.CheckoutHolds
      .SingleOrDefaultAsync(h =>
        h.CartId == cart.Id &&
        h.Status == CheckoutHoldStatuses.Active,
        ct);

    if (existing is not null)
    {
      if (now > existing.ExpiresAt)
      {
        existing.Status = CheckoutHoldStatuses.Expired;
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        await DeactivateHoldItemsAsync(existing.Id, ct);
        await _db.SaveChangesAsync(ct);
      }
      else
      {
        return (true, null, existing);
      }
    }

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    try
    {
      var hold = new CheckoutHold
      {
        Id = Guid.NewGuid(),
        CartId = cart.Id,
        UserId = userId,
        Status = CheckoutHoldStatuses.Active,
        CreatedAt = now,
        UpdatedAt = now,
        ExpiresAt = now.AddMinutes(_opts.HoldInitialMinutes),
      };

      _db.CheckoutHolds.Add(hold);

      foreach (var t in holdTargets)
      {
        _db.CheckoutHoldItems.Add(new CheckoutHoldItem
        {
          Id = Guid.NewGuid(),
          HoldId = hold.Id,
          ListingId = t.ListingId,
          OfferId = t.OfferId,
          IsActive = true,
          CreatedAt = now
        });
      }

      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);

      return (true, null, hold);
    }
    catch (DbUpdateException ex) when (IsUniqueHoldConflict(ex))
    {
      await tx.RollbackAsync(ct);
      return (false, "HOLD_CONFLICT", null);
    }
  }

  public async Task<(bool Ok, string? Error, CheckoutHold? Hold)> HeartbeatAsync(
    Guid holdId,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND", null);

    if (hold.Status != CheckoutHoldStatuses.Active)
      return (false, "HOLD_NOT_ACTIVE", null);

    if (now > hold.ExpiresAt)
    {
      hold.Status = CheckoutHoldStatuses.Expired;
      hold.UpdatedAt = now;

      await DeactivateHoldItemsAsync(hold.Id, ct);

      await _db.SaveChangesAsync(ct);

      return (false, "HOLD_EXPIRED", null);
    }


    var candidate = now.AddMinutes(_opts.HoldInitialMinutes);
    var max = hold.CreatedAt.AddMinutes(_opts.HoldMaxMinutes);

    hold.ExpiresAt = candidate < max ? candidate : max;
    hold.UpdatedAt = now;

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

    // Winner selection
    hold.CompletedAt = now;
    hold.PaymentReference = paymentReference;
    hold.Status = CheckoutHoldStatuses.Completed;
    hold.UpdatedAt = now;

    // ✅ Mark listing(s) SOLD and disable offer(s)
    await MarkHoldItemsAsSoldAsync(hold.Id, now, ct);

    // Deactivate hold items (keeps your unique constraint happy)
    await DeactivateHoldItemsAsync(hold.Id, ct);

    // Cart becomes checked out
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
      // Another webhook already completed a hold for this cart (first wins).
      return (false, "PAYMENT_ALREADY_COMPLETED");
    }
  }

  private static bool IsUniqueHoldConflict(DbUpdateException ex)
  {
    if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
      return true;

    var inner = ex.InnerException;
    while (inner is not null)
    {
      if (inner is PostgresException p && p.SqlState == PostgresErrorCodes.UniqueViolation)
        return true;

      inner = inner.InnerException;
    }

    return false;
  }

  private async Task DeactivateHoldItemsAsync(Guid holdId, CancellationToken ct)
  {
    var items = await _db.CheckoutHoldItems
      .Where(i => i.HoldId == holdId && i.IsActive)
      .ToListAsync(ct);

    if (items.Count == 0) return;

    foreach (var i in items)
      i.IsActive = false;
  }

  private async Task MarkHoldItemsAsSoldAsync(Guid holdId, DateTimeOffset now, CancellationToken ct)
  {
    var holdItems = await _db.CheckoutHoldItems
  .Where(i => i.HoldId == holdId)   // <-- remove && i.IsActive
  .Select(i => new { i.ListingId, i.OfferId })
  .ToListAsync(ct);


    if (holdItems.Count == 0) return;

    var listingIds = holdItems.Select(x => x.ListingId).Distinct().ToList();
    var offerIds = holdItems.Select(x => x.OfferId).Distinct().ToList();

    var listings = await _db.Listings.Where(l => listingIds.Contains(l.Id)).ToListAsync(ct);
    foreach (var l in listings)
    {
      l.Status = MineralKingdom.Contracts.Listings.ListingStatuses.Sold;
      l.QuantityAvailable = 0; // qty=1 world for now
      l.UpdatedAt = now;
    }

    var offers = await _db.StoreOffers
      .Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null)
      .ToListAsync(ct);

    foreach (var o in offers)
    {
      o.IsActive = false;
      o.EndsAt ??= now; // optional
      o.UpdatedAt = now;
    }
  }
}
