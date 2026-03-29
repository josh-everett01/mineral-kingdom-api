using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Listings;
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

    var orderIds = new[] { order.Id };
    var listingIds = order.Lines.Select(l => l.ListingId).Distinct().ToList();

    var latestPaymentByOrderId = await LoadLatestPaymentsByOrderIdAsync(orderIds, ct);
    var paymentsByOrderId = await LoadPaymentsByOrderIdAsync(orderIds, ct);
    var ledgerEntriesByOrderId = await LoadLedgerEntriesByOrderIdAsync(orderIds, ct);
    var listingSnapshotsById = await LoadListingSnapshotsByIdAsync(listingIds, ct);

    return BuildOrderDto(
      order,
      latestPaymentByOrderId,
      paymentsByOrderId,
      ledgerEntriesByOrderId,
      listingSnapshotsById);
  }

  public async Task<List<OrderDto>> ListForUserAsync(Guid userId, CancellationToken ct)
  {
    var orders = await _db.Orders
      .AsNoTracking()
      .Include(o => o.Lines)
      .Where(o => o.UserId == userId)
      .OrderByDescending(o => o.CreatedAt)
      .ToListAsync(ct);

    if (orders.Count == 0) return new List<OrderDto>();

    var orderIds = orders.Select(o => o.Id).ToList();
    var listingIds = orders
      .SelectMany(o => o.Lines)
      .Select(l => l.ListingId)
      .Distinct()
      .ToList();

    var latestPaymentByOrderId = await LoadLatestPaymentsByOrderIdAsync(orderIds, ct);
    var paymentsByOrderId = await LoadPaymentsByOrderIdAsync(orderIds, ct);
    var ledgerEntriesByOrderId = await LoadLedgerEntriesByOrderIdAsync(orderIds, ct);
    var listingSnapshotsById = await LoadListingSnapshotsByIdAsync(listingIds, ct);

    return orders
      .Select(order => BuildOrderDto(
        order,
        latestPaymentByOrderId,
        paymentsByOrderId,
        ledgerEntriesByOrderId,
        listingSnapshotsById))
      .ToList();
  }

  private OrderDto BuildOrderDto(
    Order order,
    IReadOnlyDictionary<Guid, PaymentSummary> latestPaymentByOrderId,
    IReadOnlyDictionary<Guid, List<PaymentHistoryEntry>> paymentsByOrderId,
    IReadOnlyDictionary<Guid, List<LedgerHistoryEntry>> ledgerEntriesByOrderId,
    IReadOnlyDictionary<Guid, ListingSnapshot> listingSnapshotsById)
  {
    latestPaymentByOrderId.TryGetValue(order.Id, out var latestPayment);
    paymentsByOrderId.TryGetValue(order.Id, out var paymentEntries);
    ledgerEntriesByOrderId.TryGetValue(order.Id, out var ledgerEntries);

    var statusHistory = BuildStatusHistory(
      order,
      paymentEntries ?? new List<PaymentHistoryEntry>(),
      ledgerEntries ?? new List<LedgerHistoryEntry>());

    var lines = order.Lines
      .OrderBy(l => l.CreatedAt)
      .Select(line =>
      {
        listingSnapshotsById.TryGetValue(line.ListingId, out var listing);

        var fallbackTitle = "Listing";

        return new OrderLineDto(
          line.Id,
          line.OfferId,
          line.ListingId,
          listing?.ListingSlug,
          listing?.Title ?? fallbackTitle,
          listing?.PrimaryImageUrl,
          listing?.MineralName,
          listing?.Locality,
          line.UnitPriceCents,
          line.UnitDiscountCents,
          line.UnitFinalPriceCents,
          line.Quantity,
          line.LineSubtotalCents,
          line.LineDiscountCents,
          line.LineTotalCents
        );
      })
      .ToList();

    return new OrderDto(
      order.Id,
      order.UserId,
      order.OrderNumber,
      order.SourceType,
      order.AuctionId,
      order.CreatedAt,
      order.UpdatedAt,
      order.PaymentDueAt,
      order.SubtotalCents,
      order.DiscountTotalCents,
      order.TotalCents,
      order.CurrencyCode,
      order.Status,
      latestPayment?.Status,
      latestPayment?.Provider,
      order.PaidAt,
      order.FulfillmentGroupId,
      statusHistory,
      lines
    );
  }

  private OrderStatusHistoryDto BuildStatusHistory(
    Order order,
    IReadOnlyList<PaymentHistoryEntry> payments,
    IReadOnlyList<LedgerHistoryEntry> ledgerEntries)
  {
    var entries = new List<OrderTimelineEntryDto>
    {
      new(
        Type: "ORDER_CREATED",
        Title: "Order created",
        Description: $"Order {order.OrderNumber} was created.",
        OccurredAt: order.CreatedAt)
    };

    if (order.PaymentDueAt is not null &&
        string.Equals(order.Status, "AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase))
    {
      entries.Add(new OrderTimelineEntryDto(
        Type: "PAYMENT_PENDING",
        Title: "Awaiting payment",
        Description: $"Payment is due by {order.PaymentDueAt.Value:MMM d, yyyy h:mm tt} UTC.",
        OccurredAt: order.UpdatedAt >= order.CreatedAt ? order.UpdatedAt : order.CreatedAt));
    }

    foreach (var payment in payments.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id))
    {
      var mapped = MapPaymentTimelineEntry(payment);
      if (mapped is not null)
      {
        entries.Add(mapped);
      }
    }

    foreach (var ledger in ledgerEntries.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id))
    {
      var mapped = MapLedgerTimelineEntry(ledger);
      if (mapped is not null)
      {
        entries.Add(mapped);
      }
    }

    if (order.PaidAt is not null &&
        !entries.Any(x => x.Type == "READY_TO_FULFILL"))
    {
      entries.Add(new OrderTimelineEntryDto(
        Type: "READY_TO_FULFILL",
        Title: "Ready to fulfill",
        Description: "Payment has been confirmed and the order is ready for fulfillment.",
        OccurredAt: order.PaidAt.Value));
    }

    var ordered = entries
      .OrderBy(x => x.OccurredAt)
      .ThenBy(x => x.Type, StringComparer.Ordinal)
      .ToList();

    return new OrderStatusHistoryDto(ordered);
  }

  private static OrderTimelineEntryDto? MapPaymentTimelineEntry(PaymentHistoryEntry payment)
  {
    return payment.Status.ToUpperInvariant() switch
    {
      "CREATED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_PENDING",
        Title: "Payment started",
        Description: $"A {payment.Provider} payment was started.",
        OccurredAt: payment.CreatedAt),

      "REDIRECTED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_REDIRECTED",
        Title: "Redirected to payment provider",
        Description: $"The buyer was redirected to {payment.Provider}.",
        OccurredAt: payment.CreatedAt),

      "SUCCEEDED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_SUCCEEDED",
        Title: "Payment confirmed",
        Description: $"{payment.Provider} confirmed the payment.",
        OccurredAt: payment.CreatedAt),

      "FAILED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_FAILED",
        Title: "Payment failed",
        Description: $"{payment.Provider} reported a failed payment attempt.",
        OccurredAt: payment.CreatedAt),

      _ => null
    };
  }

  private static OrderTimelineEntryDto? MapLedgerTimelineEntry(LedgerHistoryEntry ledger)
  {
    return ledger.EventType.ToUpperInvariant() switch
    {
      "ORDER_CREATED" => null, // already represented from the order row itself

      "PAYMENT_SUCCEEDED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_SUCCEEDED",
        Title: "Payment confirmed",
        Description: "Backend payment confirmation was recorded.",
        OccurredAt: ledger.CreatedAt),

      "ORDER_PAID" => new OrderTimelineEntryDto(
        Type: "READY_TO_FULFILL",
        Title: "Ready to fulfill",
        Description: "The order transitioned into a paid state.",
        OccurredAt: ledger.CreatedAt),

      "ORDER_READY_TO_FULFILL" => new OrderTimelineEntryDto(
        Type: "READY_TO_FULFILL",
        Title: "Ready to fulfill",
        Description: "The order is ready for fulfillment.",
        OccurredAt: ledger.CreatedAt),

      "ORDER_PAYMENT_DUE_EXTENDED" => new OrderTimelineEntryDto(
        Type: "PAYMENT_DUE_EXTENDED",
        Title: "Payment window extended",
        Description: "The order payment due date was extended.",
        OccurredAt: ledger.CreatedAt),

      _ => new OrderTimelineEntryDto(
        Type: ledger.EventType.ToUpperInvariant(),
        Title: HumanizeEventType(ledger.EventType),
        Description: null,
        OccurredAt: ledger.CreatedAt)
    };
  }

  private async Task<Dictionary<Guid, PaymentSummary>> LoadLatestPaymentsByOrderIdAsync(
    IReadOnlyCollection<Guid> orderIds,
    CancellationToken ct)
  {
    if (orderIds.Count == 0) return new Dictionary<Guid, PaymentSummary>();

    var rows = await _db.OrderPayments.AsNoTracking()
      .Where(p => orderIds.Contains(p.OrderId))
      .OrderByDescending(p => p.CreatedAt)
      .ThenByDescending(p => p.Id)
      .Select(p => new PaymentSummary(
        p.OrderId,
        p.Status,
        p.Provider,
        p.CreatedAt))
      .ToListAsync(ct);

    return rows
      .GroupBy(x => x.OrderId)
      .ToDictionary(g => g.Key, g => g.First());
  }

  private async Task<Dictionary<Guid, List<PaymentHistoryEntry>>> LoadPaymentsByOrderIdAsync(
    IReadOnlyCollection<Guid> orderIds,
    CancellationToken ct)
  {
    if (orderIds.Count == 0) return new Dictionary<Guid, List<PaymentHistoryEntry>>();

    var rows = await _db.OrderPayments.AsNoTracking()
      .Where(p => orderIds.Contains(p.OrderId))
      .OrderBy(p => p.CreatedAt)
      .ThenBy(p => p.Id)
      .Select(p => new PaymentHistoryEntry(
        p.Id,
        p.OrderId,
        p.Status,
        p.Provider,
        p.CreatedAt))
      .ToListAsync(ct);

    return rows
      .GroupBy(x => x.OrderId)
      .ToDictionary(g => g.Key, g => g.ToList());
  }

  private async Task<Dictionary<Guid, List<LedgerHistoryEntry>>> LoadLedgerEntriesByOrderIdAsync(
    IReadOnlyCollection<Guid> orderIds,
    CancellationToken ct)
  {
    if (orderIds.Count == 0) return new Dictionary<Guid, List<LedgerHistoryEntry>>();

    var rows = await _db.OrderLedgerEntries.AsNoTracking()
      .Where(e => orderIds.Contains(e.OrderId))
      .OrderBy(e => e.CreatedAt)
      .ThenBy(e => e.Id)
      .Select(e => new LedgerHistoryEntry(
        e.Id,
        e.OrderId,
        e.EventType,
        e.CreatedAt))
      .ToListAsync(ct);

    return rows
      .GroupBy(x => x.OrderId)
      .ToDictionary(g => g.Key, g => g.ToList());
  }

  private async Task<Dictionary<Guid, ListingSnapshot>> LoadListingSnapshotsByIdAsync(
    IReadOnlyCollection<Guid> listingIds,
    CancellationToken ct)
  {
    if (listingIds.Count == 0) return new Dictionary<Guid, ListingSnapshot>();

    var listings = await _db.Listings.AsNoTracking()
      .Where(l => listingIds.Contains(l.Id))
      .Select(l => new
      {
        l.Id,
        l.Title,
        l.LocalityDisplay,
        MineralName = l.PrimaryMineral != null ? l.PrimaryMineral.Name : null
      })
      .ToListAsync(ct);

    var primaryMedia = await _db.ListingMedia.AsNoTracking()
      .Where(m =>
        listingIds.Contains(m.ListingId) &&
        m.DeletedAt == null &&
        m.MediaType == ListingMediaTypes.Image &&
        m.Status == ListingMediaStatuses.Ready)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .ThenBy(m => m.CreatedAt)
      .Select(m => new
      {
        m.ListingId,
        m.Url
      })
      .ToListAsync(ct);

    var primaryImageByListingId = primaryMedia
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.First().Url);

    return listings.ToDictionary(
      x => x.Id,
      x =>
      {
        var title = string.IsNullOrWhiteSpace(x.Title) ? "Listing" : x.Title.Trim();

        return new ListingSnapshot(
          x.Id,
          Slugify(title),
          title,
          primaryImageByListingId.TryGetValue(x.Id, out var imageUrl) ? imageUrl : null,
          x.MineralName,
          x.LocalityDisplay
        );
      });
  }

  private static string HumanizeEventType(string eventType)
  {
    if (string.IsNullOrWhiteSpace(eventType)) return "Order updated";

    var normalized = eventType
      .Trim()
      .Replace("_", " ")
      .ToLowerInvariant();

    return char.ToUpperInvariant(normalized[0]) + normalized[1..];
  }

  private static string Slugify(string value)
  {
    if (string.IsNullOrWhiteSpace(value)) return "listing";

    return value
      .Trim()
      .ToLowerInvariant()
      .Replace(" ", "-");
  }

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }

  private sealed record PaymentSummary(
    Guid OrderId,
    string Status,
    string Provider,
    DateTimeOffset CreatedAt);

  private sealed record PaymentHistoryEntry(
    Guid Id,
    Guid OrderId,
    string Status,
    string Provider,
    DateTimeOffset CreatedAt);

  private sealed record LedgerHistoryEntry(
    Guid Id,
    Guid OrderId,
    string EventType,
    DateTimeOffset CreatedAt);

  private sealed record ListingSnapshot(
    Guid ListingId,
    string ListingSlug,
    string Title,
    string? PrimaryImageUrl,
    string? MineralName,
    string? Locality);
}