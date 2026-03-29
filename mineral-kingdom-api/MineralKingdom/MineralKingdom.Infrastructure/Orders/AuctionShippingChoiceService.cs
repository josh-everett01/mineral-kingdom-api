using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class AuctionShippingChoiceService
{
  private readonly MineralKingdomDbContext _db;
  private readonly TimeProvider _time;

  public AuctionShippingChoiceService(
    MineralKingdomDbContext db,
    TimeProvider time)
  {
    _db = db;
    _time = time;
  }

  public async Task<(bool Ok, string? Error, AuctionShippingChoiceResponse? Response)> SetChoiceAsync(
    Guid orderId,
    Guid userId,
    string shippingMode,
    CancellationToken ct)
  {
    var now = _time.GetUtcNow();

    var normalizedMode = (shippingMode ?? string.Empty).Trim().ToUpperInvariant();
    if (normalizedMode != AuctionShippingModes.ShipNow &&
        normalizedMode != AuctionShippingModes.OpenBox)
    {
      return (false, "INVALID_SHIPPING_MODE", null);
    }

    var order = await _db.Orders
      .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    if (order is null)
      return (false, "ORDER_NOT_FOUND", null);

    if (order.UserId != userId)
      return (false, "ORDER_NOT_FOUND", null);

    if (!string.Equals(order.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_AUCTION", null);

    if (!string.Equals(order.Status, "AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_AWAITING_PAYMENT", null);

    if (order.PaymentDueAt is not null && now > order.PaymentDueAt.Value)
      return (false, "PAYMENT_WINDOW_EXPIRED", null);

    if (normalizedMode == AuctionShippingModes.ShipNow)
    {
      if (order.AuctionId is null)
        return (false, "AUCTION_NOT_FOUND", null);

      var auction = await _db.Auctions
        .AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == order.AuctionId.Value, ct);

      if (auction is null)
        return (false, "AUCTION_NOT_FOUND", null);

      if (auction.QuotedShippingCents is null || auction.QuotedShippingCents.Value < 0)
        return (false, "QUOTED_SHIPPING_REQUIRED", null);

      order.ShippingMode = AuctionShippingModes.ShipNow;
      order.ShippingAmountCents = auction.QuotedShippingCents.Value;
      order.TotalCents = order.SubtotalCents - order.DiscountTotalCents + order.ShippingAmountCents;
    }
    else
    {
      order.ShippingMode = AuctionShippingModes.OpenBox;
      order.ShippingAmountCents = 0;
      order.TotalCents = order.SubtotalCents - order.DiscountTotalCents;
    }

    order.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return (true, null, new AuctionShippingChoiceResponse(
      OrderId: order.Id,
      ShippingMode: order.ShippingMode,
      SubtotalCents: order.SubtotalCents,
      DiscountTotalCents: order.DiscountTotalCents,
      ShippingAmountCents: order.ShippingAmountCents,
      TotalCents: order.TotalCents,
      CurrencyCode: order.CurrencyCode,
      AuctionId: order.AuctionId,
      QuotedShippingCents: normalizedMode == AuctionShippingModes.ShipNow
        ? order.ShippingAmountCents
        : null
    ));
  }
}