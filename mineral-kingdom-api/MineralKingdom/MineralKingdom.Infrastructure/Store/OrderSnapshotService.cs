using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Store;

public sealed class OrderSnapshotService
{
  private readonly MineralKingdomDbContext _db;

  public OrderSnapshotService(MineralKingdomDbContext db) => _db = db;

  public async Task<(bool Ok, string? Error, Guid? OrderId)> CreateDraftOrderAsync(
    Guid? userId,
    CreateOrderRequest req,
    CancellationToken ct)
  {
    if (req.Lines is null || req.Lines.Count == 0)
      return (false, "LINES_REQUIRED", null);

    if (req.Lines.Any(x => x.Quantity <= 0))
      return (false, "QUANTITY_INVALID", null);

    // load offers (must be active and not deleted)
    var offerIds = req.Lines.Select(x => x.OfferId).Distinct().ToList();

    var now = DateTimeOffset.UtcNow;

    var offers = await _db.StoreOffers.AsNoTracking()
      .Where(o =>
        offerIds.Contains(o.Id) &&
        o.DeletedAt == null &&
        o.IsActive &&
        (o.StartsAt == null || o.StartsAt <= now) &&
        (o.EndsAt == null || o.EndsAt >= now))
      .ToListAsync(ct);

    if (offers.Count != offerIds.Count)
      return (false, "OFFER_NOT_FOUND_OR_INACTIVE", null);

    // create order + snapshot lines
    var orderId = Guid.NewGuid();
    var createdAt = now;

    var order = new Order
    {
      Id = orderId,
      UserId = userId,
      GuestEmail = null,

      OrderNumber = GenerateOrderNumber(now),
      CheckoutHoldId = null,

      SourceType = "STORE",
      AuctionId = null,
      PaymentDueAt = null,

      CurrencyCode = "USD",
      Status = "DRAFT",
      CreatedAt = createdAt,
      UpdatedAt = createdAt
    };

    var lines = new List<OrderLine>();
    long subtotal = 0;
    long discountTotal = 0;
    long total = 0;

    foreach (var lineReq in req.Lines)
    {
      var offer = offers.Single(o => o.Id == lineReq.OfferId);

      // defensive validation in case DB contains invalid offer
      var (ok, err) = DiscountPricing.Validate(
        offer.PriceCents,
        offer.DiscountType,
        offer.DiscountCents,
        offer.DiscountPercentBps);

      if (!ok)
        return (false, err ?? "OFFER_INVALID", null);

      var unitPrice = offer.PriceCents;
      var unitFinal = DiscountPricing.ComputeEffectivePriceCents(
        offer.PriceCents,
        offer.DiscountType,
        offer.DiscountCents,
        offer.DiscountPercentBps);

      var unitDiscount = unitPrice - unitFinal;
      if (unitDiscount < 0) unitDiscount = 0;

      var qty = lineReq.Quantity;

      var lineSubtotal = unitPrice * (long)qty;
      var lineDiscount = unitDiscount * (long)qty;
      var lineTotal = unitFinal * (long)qty;

      subtotal += lineSubtotal;
      discountTotal += lineDiscount;
      total += lineTotal;

      lines.Add(new OrderLine
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        OfferId = offer.Id,
        ListingId = offer.ListingId,

        UnitPriceCents = unitPrice,
        UnitDiscountCents = unitDiscount,
        UnitFinalPriceCents = unitFinal,

        Quantity = qty,

        LineSubtotalCents = (int)lineSubtotal,
        LineDiscountCents = (int)lineDiscount,
        LineTotalCents = (int)lineTotal,

        CreatedAt = createdAt,
        UpdatedAt = createdAt
      });
    }

    order.SubtotalCents = (int)subtotal;
    order.DiscountTotalCents = (int)discountTotal;
    order.TotalCents = (int)total;

    // attach
    order.Lines = lines;

    _db.Orders.Add(order);
    await _db.SaveChangesAsync(ct);

    return (true, null, orderId);
  }

  public async Task<OrderDto?> GetOrderAsync(Guid orderId, CancellationToken ct)
  {
    var order = await _db.Orders.AsNoTracking()
      .Include(o => o.Lines)
      .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    if (order is null) return null;

    return new OrderDto(
  order.Id,
  order.UserId,
  order.OrderNumber,
  order.SourceType,
  order.AuctionId,
  order.PaymentDueAt,
  order.SubtotalCents,
  order.DiscountTotalCents,
  order.TotalCents,
  order.CurrencyCode,
  order.Status,
  order.Lines
    .OrderBy(l => l.CreatedAt)
    .Select(l => new OrderLineDto(
      l.Id,
      l.OfferId,
      l.ListingId,
      l.UnitPriceCents,
      l.UnitDiscountCents,
      l.UnitFinalPriceCents,
      l.Quantity,
      l.LineSubtotalCents,
      l.LineDiscountCents,
      l.LineTotalCents
    ))
    .ToList()
);
  }

  public async Task<List<OrderDto>> ListForUserAsync(Guid userId, CancellationToken ct)
  {
    var orders = await _db.Orders
      .AsNoTracking()
      .Include(o => o.Lines)
      .Where(o => o.UserId == userId)
      .OrderByDescending(o => o.CreatedAt)
      .ToListAsync(ct);

    return orders.Select(order => new OrderDto(
      order.Id,
      order.UserId,
      order.OrderNumber,
      order.SourceType,
      order.AuctionId,
      order.PaymentDueAt,
      order.SubtotalCents,
      order.DiscountTotalCents,
      order.TotalCents,
      order.CurrencyCode,
      order.Status,
      order.Lines
        .OrderBy(l => l.CreatedAt)
        .Select(l => new OrderLineDto(
          l.Id,
          l.OfferId,
          l.ListingId,
          l.UnitPriceCents,
          l.UnitDiscountCents,
          l.UnitFinalPriceCents,
          l.Quantity,
          l.LineSubtotalCents,
          l.LineDiscountCents,
          l.LineTotalCents
        ))
        .ToList()
    )).ToList();
  }

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }

}
