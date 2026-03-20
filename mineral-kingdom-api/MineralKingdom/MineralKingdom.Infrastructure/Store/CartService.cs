using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store.Realtime;

namespace MineralKingdom.Infrastructure.Store;

public sealed class CartService
{
  private readonly MineralKingdomDbContext _db;

  private readonly ICartRealtimePublisher _cartRealtimePublisher;
  public CartService(MineralKingdomDbContext db, ICartRealtimePublisher cartRealtimePublisher)
  {
    _db = db;
    _cartRealtimePublisher = cartRealtimePublisher;
  }

  public async Task<Cart> GetOrCreateAsync(
  Guid? userId,
  Guid? cartId,
  DateTimeOffset now,
  CancellationToken ct)
  {
    Cart? cart = null;

    if (cartId.HasValue)
    {
      var cartById = await _db.Carts
        .Include(c => c.Lines)
        .SingleOrDefaultAsync(c => c.Id == cartId.Value, ct);

      if (cartById is not null)
      {
        // Guest cart cookies should only bind to ACTIVE carts.
        // If the referenced cart has already been checked out (or otherwise
        // left ACTIVE state), treat it as unusable and fall through to create
        // a fresh active cart.
        if (cartById.Status == CartStatuses.Active)
        {
          cart = cartById;
        }
        else if (userId.HasValue && cartById.UserId == userId)
        {
          // For authenticated users, ignore the stale/non-active cart id and
          // fall through to resolve their latest ACTIVE cart below.
          cart = null;
        }
        else
        {
          cart = null;
        }
      }
    }

    if (cart is null && userId.HasValue)
    {
      cart = await _db.Carts
        .Include(c => c.Lines)
        .Where(c => c.UserId == userId && c.Status == CartStatuses.Active)
        .OrderByDescending(c => c.UpdatedAt)
        .FirstOrDefaultAsync(ct);
    }

    if (cart is not null)
      return cart;

    cart = new Cart
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      Status = CartStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Carts.Add(cart);
    await _db.SaveChangesAsync(ct);
    return cart;
  }

  public async Task<CartDto> ToDtoAsync(Cart cart, CancellationToken ct)
  {
    var cartLineRows = await _db.CartLines
      .AsNoTracking()
      .Where(x => x.CartId == cart.Id)
      .Select(x => new
      {
        x.OfferId,
        x.Quantity
      })
      .ToListAsync(ct);

    var offerIds = cartLineRows.Select(x => x.OfferId).Distinct().ToList();

    var offers = await _db.StoreOffers
        .AsNoTracking()
        .Where(x => offerIds.Contains(x.Id) && x.DeletedAt == null)
        .Select(x => new
        {
          x.Id,
          x.ListingId,
          x.PriceCents,
          x.DiscountType,
          x.DiscountCents,
          x.DiscountPercentBps,
          x.IsActive
        })
        .ToListAsync(ct);

    var listingIds = offers.Select(x => x.ListingId).Distinct().ToList();

    var listings = await _db.Listings
      .AsNoTracking()
      .Where(x => listingIds.Contains(x.Id))
      .Select(x => new
      {
        x.Id,
        x.Title,
        x.QuantityAvailable
      })
      .ToListAsync(ct);

    var listingMedia = await _db.ListingMedia
      .AsNoTracking()
      .Where(x => listingIds.Contains(x.ListingId))
      .OrderBy(x => x.SortOrder)
      .Select(x => new
      {
        x.ListingId,
        x.Url,
        x.IsPrimary,
        x.SortOrder
      })
      .ToListAsync(ct);

    var listingById = listings.ToDictionary(x => x.Id);
    var primaryMediaByListingId = listingMedia
      .GroupBy(x => x.ListingId)
      .ToDictionary(
        g => g.Key,
        g => g.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.SortOrder).FirstOrDefault()?.Url
      );

    var offerById = offers.ToDictionary(x => x.Id);

    var lines = new List<CartLineDto>();

    foreach (var row in cartLineRows)
    {
      if (!offerById.TryGetValue(row.OfferId, out var offer))
        continue;

      if (!listingById.TryGetValue(offer.ListingId, out var listing))
        continue;

      var discount = StoreOfferService.ComputeUnitDiscountCents(new StoreOffer
      {
        PriceCents = offer.PriceCents,
        DiscountType = offer.DiscountType,
        DiscountCents = offer.DiscountCents,
        DiscountPercentBps = offer.DiscountPercentBps
      });

      var effective = offer.PriceCents - discount;
      var listingTitle = listing.Title ?? "listing";

      lines.Add(new CartLineDto(
        OfferId: offer.Id,
        ListingId: listing.Id,
        ListingHref: $"/listing/{Slugify(listingTitle)}-{listing.Id}",
        Title: listingTitle,
        PrimaryImageUrl: primaryMediaByListingId.TryGetValue(listing.Id, out var imageUrl) ? imageUrl : null,
        Quantity: row.Quantity,
        QuantityAvailable: listing.QuantityAvailable,
        PriceCents: offer.PriceCents,
        EffectivePriceCents: effective,
        CanUpdateQuantity: false
      ));
    }

    var notices = await _db.CartNotices
      .AsNoTracking()
      .Where(x => x.CartId == cart.Id && x.DismissedAt == null)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new CartNoticeDto(
        x.Id,
        x.Type,
        x.Message,
        x.OfferId,
        x.ListingId,
        x.CreatedAt,
        x.DismissedAt
      ))
      .ToListAsync(ct);

    var subtotalCents = checked(lines.Sum(x => x.EffectivePriceCents * x.Quantity));

    return new CartDto(
      CartId: cart.Id,
      UserId: cart.UserId,
      Status: cart.Status,
      SubtotalCents: subtotalCents,
      Warnings: new[]
      {
        "Items in your cart are not reserved. Availability is confirmed at checkout."
      },
      Notices: notices,
      Lines: lines
    );
  }

  public async Task<IReadOnlyCollection<Guid>> RemoveSoldOfferFromOtherActiveCartsAsync(
  Guid purchasedCartId,
  Guid offerId,
  Guid listingId,
  string listingTitle,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var affectedLines = await _db.CartLines
      .Include(x => x.Cart)
      .Where(x =>
        x.OfferId == offerId &&
        x.CartId != purchasedCartId &&
        x.Cart != null &&
        x.Cart.Status == CartStatuses.Active)
      .ToListAsync(ct);

    if (affectedLines.Count == 0)
      return Array.Empty<Guid>();

    var affectedCartIds = affectedLines
      .Select(x => x.CartId)
      .Distinct()
      .ToList();

    _db.CartLines.RemoveRange(affectedLines);

    var carts = await _db.Carts
      .Where(x => affectedCartIds.Contains(x.Id))
      .ToListAsync(ct);

    foreach (var cart in carts)
    {
      cart.UpdatedAt = now;
    }

    foreach (var cartId in affectedCartIds)
    {
      var alreadyExists = await _db.CartNotices.AnyAsync(x =>
        x.CartId == cartId &&
        x.Type == CartNoticeTypes.ItemRemovedSold &&
        x.OfferId == offerId &&
        x.DismissedAt == null,
        ct);

      if (alreadyExists)
        continue;

      _db.CartNotices.Add(new CartNotice
      {
        Id = Guid.NewGuid(),
        CartId = cartId,
        Type = CartNoticeTypes.ItemRemovedSold,
        Message = $"\"{listingTitle}\" was purchased by another customer and has been removed from your cart.",
        OfferId = offerId,
        ListingId = listingId,
        CreatedAt = now
      });
    }

    return affectedCartIds;
  }

  public async Task<(bool Ok, string? Error)> DismissNoticeAsync(
  Guid cartId,
  Guid noticeId,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var notice = await _db.CartNotices
      .SingleOrDefaultAsync(x => x.Id == noticeId && x.CartId == cartId, ct);

    if (notice == null)
      return (false, "NOTICE_NOT_FOUND");

    if (notice.DismissedAt != null)
      return (true, null);

    notice.DismissedAt = now;
    await _db.SaveChangesAsync(ct);
    await _cartRealtimePublisher.PublishCartAsync(cartId, now, ct);
    return (true, null);
  }

  private static string Slugify(string value)
  {
    return value
      .Trim()
      .ToLowerInvariant()
      .Replace(" ", "-");
  }

  public async Task<(bool Ok, string? Error)> UpsertLineAsync(
    Guid cartId,
    Guid? userId,
    Guid offerId,
    int quantity,
    DateTimeOffset now,
    CancellationToken ct)
  {
    if (quantity <= 0 || quantity > 99) return (false, "INVALID_QUANTITY");

    var cart = await _db.Carts
      .Include(c => c.Lines)
      .SingleOrDefaultAsync(c => c.Id == cartId, ct);

    if (cart is null) return (false, "CART_NOT_FOUND");
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE");

    var offer = await _db.StoreOffers.AsNoTracking()
      .SingleOrDefaultAsync(o => o.Id == offerId && o.DeletedAt == null, ct);

    if (offer is null) return (false, "OFFER_NOT_FOUND");

    var listing = await _db.Listings.AsNoTracking()
      .SingleOrDefaultAsync(l => l.Id == offer.ListingId, ct);

    if (listing is null) return (false, "LISTING_NOT_FOUND");

    // v1 rule: most listings are unique 1-of-1 items
    var normalizedQuantity = listing.QuantityAvailable <= 1 ? 1 : quantity;

    var line = cart.Lines.SingleOrDefault(x => x.OfferId == offerId);
    if (line is null)
    {
      line = new CartLine
      {
        Id = Guid.NewGuid(),
        CartId = cart.Id,
        OfferId = offerId,
        Quantity = normalizedQuantity,
        CreatedAt = now,
        UpdatedAt = now
      };

      _db.CartLines.Add(line);
    }
    else
    {
      line.Quantity = normalizedQuantity;
      line.UpdatedAt = now;
    }

    cart.UpdatedAt = now;
    await _db.SaveChangesAsync(ct);

    await _cartRealtimePublisher.PublishCartAsync(cart.Id, now, ct);

    return (true, null);
  }

  public async Task<Cart?> GetCartForResponseAsync(Guid cartId, Guid? userId, CancellationToken ct)
  {
    var cart = await _db.Carts
      .Include(c => c.Lines)
      .SingleOrDefaultAsync(c => c.Id == cartId, ct);

    if (cart is null) return null;

    if (cart.UserId is not null && cart.UserId != userId) return null;
    if (cart.UserId is null && userId is not null) return null;

    return cart;
  }

  public async Task<(bool Ok, string? Error)> RemoveLineAsync(Cart cart, Guid offerId, DateTimeOffset now, CancellationToken ct)
  {
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE");

    var line = cart.Lines.SingleOrDefault(x => x.OfferId == offerId);
    if (line is null) return (true, null);

    _db.CartLines.Remove(line);
    cart.UpdatedAt = now;
    await _db.SaveChangesAsync(ct);

    await _cartRealtimePublisher.PublishCartAsync(cart.Id, now, ct);

    return (true, null);
  }
}