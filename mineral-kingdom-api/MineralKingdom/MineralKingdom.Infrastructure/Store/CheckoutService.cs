using Microsoft.EntityFrameworkCore;
using Npgsql;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Store.Realtime;

namespace MineralKingdom.Infrastructure.Store;

public sealed class CheckoutService
{
  private readonly MineralKingdomDbContext _db;
  private readonly CheckoutOptions _opts;
  private readonly CartService _cartService;
  private readonly ICartRealtimePublisher _cartRealtimePublisher;

  public CheckoutService(MineralKingdomDbContext db, IOptions<CheckoutOptions> opts, CartService cartService, ICartRealtimePublisher cartRealtimePublisher)
  {
    _db = db;
    _opts = opts.Value;
    _cartService = cartService;
    _cartRealtimePublisher = cartRealtimePublisher;
  }

  public async Task<(bool Ok, string? Error, CheckoutHold? Hold)> GetActiveCheckoutAsync(
    Cart cart,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds
      .SingleOrDefaultAsync(
        h => h.CartId == cart.Id && h.Status == CheckoutHoldStatuses.Active,
        ct);

    if (hold is null)
      return (true, null, null);

    if (hold.UserId.HasValue && hold.UserId != userId)
      return (false, "FORBIDDEN", null);

    if (now > hold.ExpiresAt)
    {
      hold.Status = CheckoutHoldStatuses.Expired;
      hold.UpdatedAt = now;

      await DeactivateHoldItemsAsync(hold.Id, ct);
      await _db.SaveChangesAsync(ct);

      return (true, null, null);
    }

    return (true, null, hold);
  }

  public async Task<(bool Ok, string? Error)> ResetActiveCheckoutAsync(
    Cart cart,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds
      .SingleOrDefaultAsync(
        h => h.CartId == cart.Id && h.Status == CheckoutHoldStatuses.Active,
        ct);

    if (hold is null)
      return (true, null);

    if (hold.UserId.HasValue && hold.UserId != userId)
      return (false, "FORBIDDEN");

    hold.Status = CheckoutHoldStatuses.Expired;
    hold.UpdatedAt = now;

    if (hold.ExpiresAt > now)
      hold.ExpiresAt = now;

    await DeactivateHoldItemsAsync(hold.Id, ct);
    await _db.SaveChangesAsync(ct);

    return (true, null);
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
        ExtensionCount = 0,
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

    if (hold.UserId.HasValue && hold.UserId != userId)
      return (false, "FORBIDDEN", null);

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

    return (true, null, hold);
  }

  public async Task<(bool Ok, string? Error, CheckoutHold? Hold)> ExtendHoldAsync(
    Guid holdId,
    Guid? userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND", null);

    if (hold.UserId.HasValue && hold.UserId != userId)
      return (false, "FORBIDDEN", null);

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

    if (hold.ExtensionCount >= _opts.HoldMaxExtensions)
      return (false, "EXTENSION_LIMIT_REACHED", null);

    var secondsRemaining = (hold.ExpiresAt - now).TotalSeconds;
    if (secondsRemaining > _opts.HoldExtendThresholdSeconds)
      return (false, "TOO_EARLY_TO_EXTEND", null);

    var candidate = hold.ExpiresAt.AddMinutes(_opts.HoldInitialMinutes);
    var max = hold.CreatedAt.AddMinutes(_opts.HoldMaxMinutes);

    hold.ExpiresAt = candidate < max ? candidate : max;
    hold.ExtensionCount += 1;
    hold.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null, hold);
  }

  public bool CanExtend(CheckoutHold hold, DateTimeOffset now)
  {
    if (hold.Status != CheckoutHoldStatuses.Active) return false;
    if (now > hold.ExpiresAt) return false;
    if (hold.ExtensionCount >= _opts.HoldMaxExtensions) return false;

    var secondsRemaining = (hold.ExpiresAt - now).TotalSeconds;
    return secondsRemaining <= _opts.HoldExtendThresholdSeconds;
  }

  public int MaxExtensions => _opts.HoldMaxExtensions;

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
    if (hold is null)
      return (false, "HOLD_NOT_FOUND");

    // True idempotency anchor:
    // if the order already exists for this hold, the business outcome is complete.
    var existingOrder = await _db.Orders.AsNoTracking()
      .SingleOrDefaultAsync(o => o.CheckoutHoldId == hold.Id, ct);

    if (existingOrder is not null)
      return (true, null);

    // Only Active and Completed are recoverable here.
    // Completed + no order can happen after a partial prior success and must be allowed to continue.
    if (hold.Status != CheckoutHoldStatuses.Active &&
        hold.Status != CheckoutHoldStatuses.Completed)
    {
      return (false, "HOLD_NOT_ACTIVE");
    }

    var affectedCartIds = new HashSet<Guid>();

    // Only execute the one-time hold completion flow if the hold is still Active.
    if (hold.Status == CheckoutHoldStatuses.Active)
    {
      if (now > hold.ExpiresAt)
      {
        hold.Status = CheckoutHoldStatuses.Expired;
        hold.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (false, "HOLD_EXPIRED");
      }

      hold.CompletedAt ??= now;

      if (string.IsNullOrWhiteSpace(hold.PaymentReference))
        hold.PaymentReference = paymentReference;

      hold.Status = CheckoutHoldStatuses.Completed;
      hold.UpdatedAt = now;

      await MarkHoldItemsAsSoldAsync(hold.Id, now, ct);
      await DeactivateHoldItemsAsync(hold.Id, ct);

      var cart = await _db.Carts.SingleAsync(c => c.Id == hold.CartId, ct);
      cart.Status = CartStatuses.CheckedOut;
      cart.UpdatedAt = now;

      var soldHoldItems = await _db.CheckoutHoldItems
        .Where(x => x.HoldId == hold.Id)
        .Select(x => new { x.OfferId, x.ListingId })
        .ToListAsync(ct);

      var soldListingIds = soldHoldItems
        .Select(x => x.ListingId)
        .Distinct()
        .ToList();

      var soldListings = await _db.Listings
        .Where(x => soldListingIds.Contains(x.Id))
        .Select(x => new { x.Id, x.Title })
        .ToListAsync(ct);

      var soldListingTitleById = soldListings.ToDictionary(x => x.Id, x => x.Title);

      foreach (var item in soldHoldItems)
      {
        var reconciledCartIds = await _cartService.RemoveSoldOfferFromOtherActiveCartsAsync(
          purchasedCartId: hold.CartId,
          offerId: item.OfferId,
          listingId: item.ListingId,
          listingTitle: soldListingTitleById.TryGetValue(item.ListingId, out var title)
            ? title ?? "Item"
            : "Item",
          now: now,
          ct: ct);

        foreach (var cartId in reconciledCartIds)
        {
          affectedCartIds.Add(cartId);
        }
      }
    }
    else
    {
      // Recoverable replay path:
      // hold is already Completed but no order exists yet.
      // Preserve original completion state, but fill missing reference if absent.
      if (string.IsNullOrWhiteSpace(hold.PaymentReference))
      {
        hold.PaymentReference = paymentReference;
        hold.UpdatedAt = now;
      }
    }

    try
    {
      // Re-check inside the transaction before creating the order in case another worker/thread got there first.
      existingOrder = await _db.Orders
        .SingleOrDefaultAsync(o => o.CheckoutHoldId == hold.Id, ct);

      if (existingOrder is null)
      {
        var order = await BuildPaidOrderFromHoldAsync(hold, now, ct);

        _db.Orders.Add(order);

        var checkoutPayment = await _db.CheckoutPayments
          .Where(p => p.HoldId == hold.Id)
          .OrderByDescending(p => p.CreatedAt)
          .ThenByDescending(p => p.Id)
          .FirstOrDefaultAsync(ct);

        if (checkoutPayment is not null)
        {
          _db.OrderPayments.Add(new OrderPayment
          {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = checkoutPayment.Provider,
            Status = "SUCCEEDED",
            ProviderCheckoutId = checkoutPayment.ProviderCheckoutId,
            ProviderPaymentId = checkoutPayment.ProviderPaymentId,
            AmountCents = order.TotalCents,
            CurrencyCode = order.CurrencyCode,
            CreatedAt = now,
            UpdatedAt = now
          });
        }

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

      await _cartRealtimePublisher.PublishCartAsync(hold.CartId, now, ct);

      foreach (var cartId in affectedCartIds)
      {
        await _cartRealtimePublisher.PublishCartAsync(cartId, now, ct);
      }

      return (true, null);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
    {
      await tx.RollbackAsync(ct);

      var existingForHold = await _db.Orders.AsNoTracking()
        .AnyAsync(o => o.CheckoutHoldId == holdId, ct);

      if (existingForHold)
        return (true, null);

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
      .Where(i => i.HoldId == holdId)
      .Select(i => new { i.ListingId, i.OfferId })
      .ToListAsync(ct);

    if (holdItems.Count == 0) return;

    var listingIds = holdItems.Select(x => x.ListingId).Distinct().ToList();
    var offerIds = holdItems.Select(x => x.OfferId).Distinct().ToList();

    var listings = await _db.Listings.Where(l => listingIds.Contains(l.Id)).ToListAsync(ct);
    foreach (var l in listings)
    {
      l.Status = ListingStatuses.Sold;
      l.QuantityAvailable = 0;
      l.UpdatedAt = now;
    }

    var offers = await _db.StoreOffers
      .Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null)
      .ToListAsync(ct);

    foreach (var o in offers)
    {
      o.IsActive = false;
      o.EndsAt ??= now;
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
      Status = "READY_TO_FULFILL",
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