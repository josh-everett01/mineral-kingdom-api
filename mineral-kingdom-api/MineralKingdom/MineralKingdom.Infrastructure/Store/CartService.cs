using Microsoft.EntityFrameworkCore;
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
    // member cart: single ACTIVE cart per user
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

      // reload with lines
      return await _db.Carts.Include(c => c.Lines).SingleAsync(c => c.Id == created.Id, ct);
    }

    // guest cart: by cartId header
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

    // Optional but recommended: validate offer exists/active
    var offerExists = await _db.StoreOffers.AsNoTracking()
      .AnyAsync(o => o.Id == offerId && o.DeletedAt == null, ct);

    if (!offerExists) return (false, "OFFER_NOT_FOUND");

    var line = cart.Lines.SingleOrDefault(x => x.OfferId == offerId);
    if (line is null)
    {
      line = new CartLine
      {
        Id = Guid.NewGuid(),
        CartId = cart.Id,
        OfferId = offerId,
        Quantity = quantity,
        CreatedAt = now,
        UpdatedAt = now
      };

      _db.CartLines.Add(line);   // <-- forces INSERT
    }
    else
    {
      line.Quantity = quantity;
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
    if (line is null) return (true, null); // idempotent

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

    // member cart: must match user
    if (cart.UserId is not null && cart.UserId != userId) return null;

    // guest cart: must be guest
    if (cart.UserId is null && userId is not null) return null;

    return cart;
  }

  public static CartDto ToDto(Cart cart) =>
    new(
      CartId: cart.Id,
      UserId: cart.UserId,
      Status: cart.Status,
      Lines: cart.Lines.Select(l => new CartLineDto(l.OfferId, l.Quantity)).ToList()
    );
}
