using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Dashboard;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Dashboard;

public sealed class DashboardService
{
  private readonly MineralKingdomDbContext _db;
  private const int Limit = 20;

  public DashboardService(MineralKingdomDbContext db) => _db = db;

  public async Task<DashboardDto> GetMyDashboardAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
  {
    var wonAuctions = await _db.Auctions.AsNoTracking()
      .Where(a => a.CurrentLeaderUserId == userId && a.Status == AuctionStatuses.ClosedPaid)
      .OrderByDescending(a => a.CloseTime)
      .Take(Limit)
      .Select(a => new DashboardWonAuctionDto(
        a.Id,
        a.ListingId,
        a.CurrentPriceCents,
        a.CloseTime,
        a.Status))
      .ToListAsync(ct);

    var unpaidOrderRows = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.SourceType == "AUCTION" &&
        o.Status == "AWAITING_PAYMENT")
      .OrderBy(o => o.PaymentDueAt)
      .ThenByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new
      {
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId,
        o.ShippingMode
      })
      .ToListAsync(ct);

    var paidOrderRows = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.Status == "READY_TO_FULFILL")
      .OrderByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new
      {
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId,
        o.ShippingMode
      })
      .ToListAsync(ct);

    var orderIdsNeedingPreview = unpaidOrderRows.Select(x => x.Id)
      .Concat(paidOrderRows.Select(x => x.Id))
      .Distinct()
      .ToList();

    var orderLineRows = await (
      from line in _db.OrderLines.AsNoTracking()
      join listing in _db.Listings.AsNoTracking()
        on line.ListingId equals listing.Id
      where orderIdsNeedingPreview.Contains(line.OrderId)
      orderby line.CreatedAt
      select new
      {
        line.OrderId,
        line.Quantity,
        listing.Id,
        listing.Title
      }
    ).ToListAsync(ct);

    var listingIds = orderLineRows.Select(x => x.Id).Distinct().ToList();

    var mediaRows = await _db.ListingMedia.AsNoTracking()
      .Where(m =>
        listingIds.Contains(m.ListingId) &&
        m.Status == ListingMediaStatuses.Ready &&
        m.DeletedAt == null)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new
      {
        m.ListingId,
        m.Url
      })
      .ToListAsync(ct);

    var previewImageByListingId = mediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.First().Url);

    var orderPreviewByOrderId = orderLineRows
      .GroupBy(x => x.OrderId)
      .ToDictionary(
        g => g.Key,
        g =>
        {
          var first = g.First();
          previewImageByListingId.TryGetValue(first.Id, out var previewImageUrl);

          return new
          {
            ItemCount = g.Sum(x => x.Quantity),
            PreviewTitle = first.Title,
            PreviewImageUrl = previewImageUrl
          };
        });

    var unpaidAuctionOrders = unpaidOrderRows
      .Select(o =>
      {
        orderPreviewByOrderId.TryGetValue(o.Id, out var preview);

        return new DashboardOrderSummaryDto(
          o.Id,
          o.OrderNumber,
          o.SourceType,
          o.Status,
          o.TotalCents,
          o.CurrencyCode,
          o.CreatedAt,
          o.PaymentDueAt,
          o.FulfillmentGroupId,
          o.ShippingMode,
          preview?.ItemCount ?? 0,
          preview?.PreviewTitle,
          preview?.PreviewImageUrl);
      })
      .ToList();

    var paidOrders = paidOrderRows
      .Select(o =>
      {
        orderPreviewByOrderId.TryGetValue(o.Id, out var preview);

        return new DashboardOrderSummaryDto(
          o.Id,
          o.OrderNumber,
          o.SourceType,
          o.Status,
          o.TotalCents,
          o.CurrencyCode,
          o.CreatedAt,
          o.PaymentDueAt,
          o.FulfillmentGroupId,
          o.ShippingMode,
          preview?.ItemCount ?? 0,
          preview?.PreviewTitle,
          preview?.PreviewImageUrl);
      })
      .ToList();

    var openBox = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.UserId == userId && g.BoxStatus == "OPEN")
      .OrderByDescending(g => g.UpdatedAt)
      .Select(g => new
      {
        g.Id,
        g.Status,
        g.UpdatedAt
      })
      .FirstOrDefaultAsync(ct);

    DashboardOpenBoxDto? openBoxDto = null;

    if (openBox is not null)
    {
      var openBoxOrderRows = await _db.Orders.AsNoTracking()
        .Where(o => o.UserId == userId && o.FulfillmentGroupId == openBox.Id)
        .OrderByDescending(o => o.CreatedAt)
        .Take(Limit)
        .Select(o => new
        {
          o.Id,
          o.OrderNumber,
          o.SourceType,
          o.Status,
          o.TotalCents,
          o.CurrencyCode,
          o.CreatedAt,
          o.PaymentDueAt,
          o.FulfillmentGroupId,
          o.ShippingMode
        })
        .ToListAsync(ct);

      var openBoxOrders = openBoxOrderRows
        .Select(o =>
        {
          orderPreviewByOrderId.TryGetValue(o.Id, out var preview);

          return new DashboardOrderSummaryDto(
            o.Id,
            o.OrderNumber,
            o.SourceType,
            o.Status,
            o.TotalCents,
            o.CurrencyCode,
            o.CreatedAt,
            o.PaymentDueAt,
            o.FulfillmentGroupId,
            o.ShippingMode,
            preview?.ItemCount ?? 0,
            preview?.PreviewTitle,
            preview?.PreviewImageUrl);
        })
        .ToList();

      openBoxDto = new DashboardOpenBoxDto(
        openBox.Id,
        openBox.Status,
        openBox.UpdatedAt,
        openBoxOrders);
    }

    var invoiceRows = await (
      from inv in _db.ShippingInvoices.AsNoTracking()
      join grp in _db.FulfillmentGroups.AsNoTracking()
        on inv.FulfillmentGroupId equals grp.Id
      where grp.UserId == userId
      orderby inv.CreatedAt descending
      select new
      {
        inv.Id,
        inv.FulfillmentGroupId,
        inv.AmountCents,
        inv.CurrencyCode,
        inv.Status,
        inv.Provider,
        inv.ProviderCheckoutId,
        inv.PaidAt,
        inv.CreatedAt
      }
    )
    .Take(Limit)
    .ToListAsync(ct);

    var fulfillmentGroupIds = invoiceRows.Select(x => x.FulfillmentGroupId).Distinct().ToList();

    var invoiceOrderRows = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.FulfillmentGroupId != null &&
        fulfillmentGroupIds.Contains(o.FulfillmentGroupId.Value))
      .OrderBy(o => o.CreatedAt)
      .Select(o => new
      {
        o.Id,
        o.OrderNumber,
        o.SourceType,
        FulfillmentGroupId = o.FulfillmentGroupId!.Value
      })
      .ToListAsync(ct);

    var invoiceOrderIds = invoiceOrderRows.Select(x => x.Id).ToList();

    var invoiceLineRows = await (
      from line in _db.OrderLines.AsNoTracking()
      join listing in _db.Listings.AsNoTracking()
        on line.ListingId equals listing.Id
      where invoiceOrderIds.Contains(line.OrderId)
      orderby line.CreatedAt
      select new
      {
        line.OrderId,
        line.Quantity,
        listing.Id,
        listing.Title
      }
    ).ToListAsync(ct);

    var invoiceListingIds = invoiceLineRows.Select(x => x.Id).Distinct().ToList();

    var invoiceMediaRows = await _db.ListingMedia.AsNoTracking()
      .Where(m =>
        invoiceListingIds.Contains(m.ListingId) &&
        m.Status == ListingMediaStatuses.Ready &&
        m.DeletedAt == null)
      .OrderByDescending(m => m.IsPrimary)
      .ThenBy(m => m.SortOrder)
      .Select(m => new
      {
        m.ListingId,
        m.Url
      })
      .ToListAsync(ct);

    var invoicePreviewImageByListingId = invoiceMediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.First().Url);

    var relatedOrdersByGroup = invoiceOrderRows
      .GroupBy(x => x.FulfillmentGroupId)
      .ToDictionary(
        g => g.Key,
        g => g.Select(x => new DashboardShippingInvoiceRelatedOrderDto(
          x.Id,
          x.OrderNumber,
          x.SourceType)).ToList());

    var orderIdsByGroup = invoiceOrderRows
      .GroupBy(x => x.FulfillmentGroupId)
      .ToDictionary(
        g => g.Key,
        g => g.Select(x => x.Id).ToHashSet());

    var invoiceLinesByOrder = invoiceLineRows
      .GroupBy(x => x.OrderId)
      .ToDictionary(g => g.Key, g => g.ToList());

    var invoices = invoiceRows
      .Select(inv =>
      {
        relatedOrdersByGroup.TryGetValue(inv.FulfillmentGroupId, out var relatedOrders);
        orderIdsByGroup.TryGetValue(inv.FulfillmentGroupId, out var groupOrderIds);

        relatedOrders ??= new List<DashboardShippingInvoiceRelatedOrderDto>();
        groupOrderIds ??= new HashSet<Guid>();

        var groupLines = invoiceLineRows.Where(x => groupOrderIds.Contains(x.OrderId)).ToList();
        var itemCount = groupLines.Sum(x => x.Quantity);

        string? previewTitle = null;
        string? previewImageUrl = null;

        var firstRelatedOrder = relatedOrders.FirstOrDefault();
        if (firstRelatedOrder is not null &&
            invoiceLinesByOrder.TryGetValue(firstRelatedOrder.OrderId, out var firstLines) &&
            firstLines.Count > 0)
        {
          var firstLine = firstLines[0];
          previewTitle = firstLine.Title;
          invoicePreviewImageByListingId.TryGetValue(firstLine.Id, out previewImageUrl);
        }

        var auctionOrderCount = relatedOrders.Count(x =>
          string.Equals(x.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase));

        var storeOrderCount = relatedOrders.Count(x =>
          string.Equals(x.SourceType, "STORE", StringComparison.OrdinalIgnoreCase));

        return new DashboardShippingInvoiceDto(
          inv.Id,
          inv.FulfillmentGroupId,
          inv.AmountCents,
          inv.CurrencyCode,
          inv.Status,
          inv.Provider,
          inv.ProviderCheckoutId,
          inv.PaidAt,
          inv.CreatedAt,
          itemCount,
          previewTitle,
          previewImageUrl,
          auctionOrderCount,
          storeOrderCount,
          relatedOrders);
      })
      .ToList();

    return new DashboardDto(
      WonAuctions: wonAuctions,
      UnpaidAuctionOrders: unpaidAuctionOrders,
      PaidOrders: paidOrders,
      OpenBox: openBoxDto,
      ShippingInvoices: invoices);
  }
}