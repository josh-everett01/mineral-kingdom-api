using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Store.Realtime;

public sealed class CartRealtimePublisher : ICartRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly CartRealtimeHub _hub;

  public CartRealtimePublisher(MineralKingdomDbContext db, CartRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishCartAsync(Guid cartId, DateTimeOffset now, CancellationToken ct)
  {
    var cart = await _db.Carts
      .AsNoTracking()
      .Where(x => x.Id == cartId)
      .Select(x => new
      {
        x.Id,
        x.Status
      })
      .SingleOrDefaultAsync(ct);

    if (cart is null)
      return;

    var pricedLines = await _db.CartLines
      .AsNoTracking()
      .Where(x => x.CartId == cartId)
      .Join(
        _db.StoreOffers.AsNoTracking().Where(x => x.DeletedAt == null),
        line => line.OfferId,
        offer => offer.Id,
        (line, offer) => new
        {
          line.Quantity,
          offer.PriceCents,
          offer.DiscountType,
          offer.DiscountCents,
          offer.DiscountPercentBps
        })
      .ToListAsync(ct);

    var subtotalCents = checked(pricedLines.Sum(x =>
    {
      var unitDiscount = StoreOfferService.ComputeUnitDiscountCents(new StoreOffer
      {
        PriceCents = x.PriceCents,
        DiscountType = x.DiscountType,
        DiscountCents = x.DiscountCents,
        DiscountPercentBps = x.DiscountPercentBps
      });

      var unitFinal = x.PriceCents - unitDiscount;
      return unitFinal * x.Quantity;
    }));

    var lineCount = pricedLines.Sum(x => x.Quantity);

    var noticeCount = await _db.CartNotices
      .AsNoTracking()
      .Where(x => x.CartId == cartId && x.DismissedAt == null)
      .CountAsync(ct);

    var snapshot = new CartRealtimeSnapshot(
      CartId: cart.Id,
      Status: cart.Status,
      SubtotalCents: subtotalCents,
      LineCount: lineCount,
      NoticeCount: noticeCount,
      EmittedAt: now);

    await _hub.PublishAsync(cartId, snapshot, ct);
  }
}