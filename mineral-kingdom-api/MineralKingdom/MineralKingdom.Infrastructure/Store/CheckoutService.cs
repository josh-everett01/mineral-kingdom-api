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
    string? guestEmail,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE", null);
    if (cart.Lines.Count == 0) return (false, "CART_EMPTY", null);

    string? normalizedGuestEmail = null;

    if (userId is null)
    {
      if (string.IsNullOrWhiteSpace(guestEmail))
        return (false, "EMAIL_REQUIRED", null);

      normalizedGuestEmail = guestEmail.Trim().ToLowerInvariant();
    }

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
        // If guest, require email match for reuse
        if (userId is null && !string.Equals(existing.GuestEmail, normalizedGuestEmail, StringComparison.Ordinal))
          return (false, "EMAIL_MISMATCH", null);

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
        GuestEmail = normalizedGuestEmail,
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
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND");

    // Idempotency: if already completed, ensure order exists and return OK
    if (hold.Status == CheckoutHoldStatuses.Completed)
    {
      var existingOrder = await _db.Orders.AsNoTracking()
        .SingleOrDefaultAsync(o => o.CheckoutHoldId == hold.Id, ct);

      if (existingOrder is not null)
        return (true, null);
      // If hold is completed but order missing (should be rare), we continue and attempt create.
    }

    if (hold.Status != CheckoutHoldStatuses.Active)
      return (false, "HOLD_NOT_ACTIVE");

    if (now > hold.ExpiresAt)
    {
      hold.Status = CheckoutHoldStatuses.Expired;
      hold.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return (false, "HOLD_EXPIRED");
    }

    // Mark hold completed
    hold.CompletedAt = now;
    hold.PaymentReference = paymentReference;
    hold.Status = CheckoutHoldStatuses.Completed;
    hold.UpdatedAt = now;

    // Mark listing(s) SOLD and disable offer(s)
    await MarkHoldItemsAsSoldAsync(hold.Id, now, ct);

    // Deactivate hold items
    await DeactivateHoldItemsAsync(hold.Id, ct);

    // Cart becomes checked out
    var cart = await _db.Carts.SingleAsync(c => c.Id == hold.CartId, ct);
    cart.Status = CartStatuses.CheckedOut;
    cart.UpdatedAt = now;

    try
    {
      // Create paid order once per hold
      var existing = await _db.Orders.AsNoTracking()
        .SingleOrDefaultAsync(o => o.CheckoutHoldId == hold.Id, ct);

      if (existing is null)
      {
        var order = await BuildPaidOrderFromHoldAsync(hold, now, ct);

        _db.Orders.Add(order);

        _db.OrderLedgerEntries.Add(new OrderLedgerEntry
        {
          Id = Guid.NewGuid(),
          OrderId = order.Id,
          EventType = "PAYMENT_SUCCEEDED",
          DataJson = $"{{\"paymentReference\":\"{paymentReference}\"}}",
          CreatedAt = now
        });

        _db.OrderLedgerEntries.Add(new OrderLedgerEntry
        {
          Id = Guid.NewGuid(),
          OrderId = order.Id,
          EventType = "ORDER_CREATED",
          DataJson = null,
          CreatedAt = now
        });
      }

      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return (true, null);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
    {
      await tx.RollbackAsync(ct);

      // If an order already exists for THIS hold, treat as webhook retry success (idempotent).
      var existingForHold = await _db.Orders.AsNoTracking()
        .AnyAsync(o => o.CheckoutHoldId == holdId, ct);

      if (existingForHold)
        return (true, null);

      // Otherwise, someone else already completed checkout for this cart / another hold won.
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

  private async Task<Order> BuildPaidOrderFromHoldAsync(
  CheckoutHold hold,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var cart = await _db.Carts
      .Include(c => c.Lines)
      .SingleAsync(c => c.Id == hold.CartId, ct);

    var offerIds = cart.Lines.Select(l => l.OfferId).Distinct().ToList();

    var offers = await _db.StoreOffers
      .AsNoTracking()
      .Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null)
      .ToListAsync(ct);

    if (offers.Count != offerIds.Count)
      throw new InvalidOperationException("OFFER_NOT_FOUND_DURING_ORDER_CREATE");

    var offerById = offers.ToDictionary(x => x.Id, x => x);

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = hold.UserId,
      GuestEmail = hold.GuestEmail,
      OrderNumber = GenerateOrderNumber(now),
      CheckoutHoldId = hold.Id,
      Status = "PAID",
      PaidAt = now,
      CurrencyCode = "USD",
      CreatedAt = now,
      UpdatedAt = now
    };

    foreach (var cartLine in cart.Lines)
    {
      var offer = offerById[cartLine.OfferId];

      var unitPrice = offer.PriceCents;
      var unitDiscountRaw = StoreOfferService.ComputeUnitDiscountCents(offer);
      var unitDiscount = Math.Clamp(unitDiscountRaw, 0, unitPrice);
      var unitFinal = unitPrice - unitDiscount;

      var qty = cartLine.Quantity;

      var lineSubtotal = checked((int)((long)unitPrice * qty));
      var lineDiscount = checked((int)((long)unitDiscount * qty));
      var lineTotal = checked((int)((long)unitFinal * qty));

      order.Lines.Add(new OrderLine
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        OfferId = offer.Id,
        ListingId = offer.ListingId,

        UnitPriceCents = unitPrice,
        UnitDiscountCents = unitDiscount,
        UnitFinalPriceCents = unitFinal,

        Quantity = qty,

        LineSubtotalCents = lineSubtotal,
        LineDiscountCents = lineDiscount,
        LineTotalCents = lineTotal,

        CreatedAt = now,
        UpdatedAt = now
      });
    }

    order.SubtotalCents = checked(order.Lines.Sum(x => x.LineSubtotalCents));
    order.DiscountTotalCents = checked(order.Lines.Sum(x => x.LineDiscountCents));
    order.TotalCents = checked(order.Lines.Sum(x => x.LineTotalCents));

    return order;
  }

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }
}
