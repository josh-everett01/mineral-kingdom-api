using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class OrderService
{
  private readonly MineralKingdomDbContext _db;

  public OrderService(MineralKingdomDbContext db) => _db = db;

  public sealed record CreateLine(Guid OfferId, int Quantity);

  public async Task<(bool Ok, string? Error, Order? Order)> CreateDraftAsync(
    Guid userId,
    List<CreateLine> lines,
    CancellationToken ct)
  {
    if (lines is null || lines.Count == 0)
      return (false, "NO_LINES", null);

    if (lines.Any(l => l.Quantity <= 0 || l.Quantity > 99))
      return (false, "INVALID_QUANTITY", null);

    var now = DateTimeOffset.UtcNow;

    // load offers
    var offerIds = lines.Select(x => x.OfferId).Distinct().ToList();

    var offers = await _db.StoreOffers
      .AsNoTracking()
      .Where(x => offerIds.Contains(x.Id) && x.DeletedAt == null && x.IsActive)
      .ToListAsync(ct);

    if (offers.Count != offerIds.Count)
      return (false, "OFFER_NOT_FOUND", null);

    // Create order
    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      GuestEmail = null,
      OrderNumber = GenerateOrderNumber(now),
      CheckoutHoldId = null,
      Status = "DRAFT",
      PaidAt = null,
      CurrencyCode = "USD",
      CreatedAt = now,
      UpdatedAt = now
    };

    var offerById = offers.ToDictionary(x => x.Id, x => x);

    foreach (var reqLine in lines)
    {
      var offer = offerById[reqLine.OfferId];

      if (!StoreOfferService.IsOfferCurrentlyValid(offer, now))
        return (false, "OFFER_NOT_ACTIVE", null);

      // Snapshot pricing
      var unitPrice = offer.PriceCents;
      var unitDiscountRaw = StoreOfferService.ComputeUnitDiscountCents(offer);

      // Clamp: discount can’t exceed unit price and can’t go below 0
      var unitDiscount = Math.Clamp(unitDiscountRaw, 0, unitPrice);

      var unitFinal = unitPrice - unitDiscount; // guaranteed >= 0


      var qty = reqLine.Quantity;

      var lineSubtotal = checked((int)((long)unitPrice * qty));
      var lineDiscount = checked((int)((long)unitDiscount * qty));
      var lineTotal = checked((int)((long)unitFinal * qty));


      // Pull listingId from offer target (for future display/shipping)
      var line = new OrderLine
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
      };

      order.Lines.Add(line);
    }

    order.SubtotalCents = checked(order.Lines.Sum(x => x.LineSubtotalCents));
    order.DiscountTotalCents = checked(order.Lines.Sum(x => x.LineDiscountCents));
    order.TotalCents = checked(order.Lines.Sum(x => x.LineTotalCents));

    _db.Orders.Add(order);
    await _db.SaveChangesAsync(ct);

    return (true, null, order);
  }

  public async Task<(bool Ok, string? Error, Order? Order)> GetAsync(Guid orderId, Guid userId, CancellationToken ct)
  {
    var order = await _db.Orders
      .Include(o => o.Lines)
      .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    if (order is null) return (false, "ORDER_NOT_FOUND", null);
    if (order.UserId != userId) return (false, "FORBIDDEN", null);

    return (true, null, order);
  }

  public async Task<OrderDto?> GetGuestOrderAsync(string orderNumber, string email, CancellationToken ct)
  {
    var order = await _db.Orders.AsNoTracking()
      .Include(o => o.Lines)
      .SingleOrDefaultAsync(o =>
        o.OrderNumber == orderNumber &&
        o.GuestEmail == email,
        ct);

    if (order is null) return null;

    return new OrderDto(
      order.Id,
      order.UserId,
      order.OrderNumber,
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

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }
}
