using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Store;

public sealed class CartService
{
  private readonly MineralKingdomDbContext _db;

  public CartService(MineralKingdomDbContext db) => _db = db;

  public async Task<Cart> GetOrCreateAsync(Guid? userId, Guid? cartId, DateTimeOffset now, CancellationToken ct)
  {
    if (userId is not null)
    {
      var existing = await _db.Carts
        .Include(c => c.Lines)
        .SingleOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatuses.Active, ct);

      if (existing is not null) return existing;

      var created = new Cart
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        Status = CartStatuses.Active,
        CreatedAt = now,
        UpdatedAt = now
      };

      _db.Carts.Add(created);
      await _db.SaveChangesAsync(ct);

      return await _db.Carts.Include(c => c.Lines).SingleAsync(c => c.Id == created.Id, ct);
    }

    if (cartId is not null)
    {
      var existing = await _db.Carts
        .Include(c => c.Lines)
        .SingleOrDefaultAsync(c => c.Id == cartId && c.UserId == null && c.Status == CartStatuses.Active, ct);

      if (existing is not null) return existing;
    }

    var guest = new Cart
    {
      Id = Guid.NewGuid(),
      UserId = null,
      Status = CartStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.Carts.Add(guest);
    await _db.SaveChangesAsync(ct);

    return await _db.Carts.Include(c => c.Lines).SingleAsync(c => c.Id == guest.Id, ct);
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

    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> RemoveLineAsync(Cart cart, Guid offerId, DateTimeOffset now, CancellationToken ct)
  {
    if (cart.Status != CartStatuses.Active) return (false, "CART_NOT_ACTIVE");

    var line = cart.Lines.SingleOrDefault(x => x.OfferId == offerId);
    if (line is null) return (true, null);

    _db.CartLines.Remove(line);
    cart.UpdatedAt = now;
    await _db.SaveChangesAsync(ct);

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

  public async Task<CartDto> ToDtoAsync(Cart cart, CancellationToken ct)
  {
    var offerIds = cart.Lines.Select(x => x.OfferId).Distinct().ToList();

    if (offerIds.Count == 0)
    {
      return new CartDto(
        CartId: cart.Id,
        UserId: cart.UserId,
        Status: cart.Status,
        SubtotalCents: 0,
        Warnings: new[]
        {
          "Items in your cart are not reserved. Availability is confirmed at checkout."
        },
        Lines: Array.Empty<CartLineDto>());
    }

    var offers = await _db.StoreOffers.AsNoTracking()
      .Where(x => offerIds.Contains(x.Id) && x.DeletedAt == null)
      .ToListAsync(ct);

    var offerById = offers.ToDictionary(x => x.Id, x => x);

    var listingIds = offers.Select(x => x.ListingId).Distinct().ToList();

    var listingRows = await (
      from listing in _db.Listings.AsNoTracking()
      where listingIds.Contains(listing.Id)
      select new
      {
        listing.Id,
        listing.Title,
        listing.QuantityAvailable
      })
      .ToListAsync(ct);

    var listingById = listingRows.ToDictionary(x => x.Id, x => x);

    var mediaRows = await _db.ListingMedia.AsNoTracking()
      .Where(x =>
        listingIds.Contains(x.ListingId) &&
        x.Status == ListingMediaStatuses.Ready &&
        x.DeletedAt == null)
      .OrderByDescending(x => x.IsPrimary)
      .ThenBy(x => x.SortOrder)
      .Select(x => new
      {
        x.ListingId,
        x.Url
      })
      .ToListAsync(ct);

    var primaryImageByListingId = mediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Url);

    var lines = cart.Lines
      .Select(line =>
      {
        if (!offerById.TryGetValue(line.OfferId, out var offer))
          return null;

        if (!listingById.TryGetValue(offer.ListingId, out var listing))
          return null;

        var effectivePriceCents = DiscountPricing.ComputeEffectivePriceCents(
          offer.PriceCents,
          offer.DiscountType,
          offer.DiscountCents,
          offer.DiscountPercentBps);

        var canUpdateQuantity = listing.QuantityAvailable > 1;

        return new CartLineDto(
          OfferId: line.OfferId,
          ListingId: listing.Id,
          ListingHref: BuildListingHref(listing.Id, listing.Title),
          Title: listing.Title ?? "Untitled listing",
          PrimaryImageUrl: primaryImageByListingId.GetValueOrDefault(listing.Id),
          Quantity: canUpdateQuantity ? line.Quantity : 1,
          QuantityAvailable: listing.QuantityAvailable,
          PriceCents: offer.PriceCents,
          EffectivePriceCents: effectivePriceCents,
          CanUpdateQuantity: canUpdateQuantity
        );
      })
      .Where(x => x is not null)
      .Cast<CartLineDto>()
      .ToList();

    var subtotalCents = lines.Sum(x => x.EffectivePriceCents * x.Quantity);

    return new CartDto(
      CartId: cart.Id,
      UserId: cart.UserId,
      Status: cart.Status,
      SubtotalCents: subtotalCents,
      Warnings: new[]
      {
        "Items in your cart are not reserved. Availability is confirmed at checkout."
      },
      Lines: lines
    );
  }

  private static string BuildListingHref(Guid listingId, string? title)
  => $"/listing/{BuildSlug(title)}-{listingId:D}";

  private static string BuildSlug(string? title)
  {
    if (string.IsNullOrWhiteSpace(title))
      return "listing";

    var chars = title.Trim().ToLowerInvariant();
    var buffer = new List<char>(chars.Length);
    var previousDash = false;

    foreach (var ch in chars)
    {
      if (char.IsLetterOrDigit(ch))
      {
        buffer.Add(ch);
        previousDash = false;
        continue;
      }

      if (previousDash)
        continue;

      buffer.Add('-');
      previousDash = true;
    }

    var slug = new string(buffer.ToArray()).Trim('-');
    return string.IsNullOrWhiteSpace(slug) ? "listing" : slug;
  }
}