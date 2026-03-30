using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class ShippingInvoicePaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IEnumerable<IShippingInvoicePaymentProvider> _providers;

  public ShippingInvoicePaymentService(MineralKingdomDbContext db, IEnumerable<IShippingInvoicePaymentProvider> providers)
  {
    _db = db;
    _providers = providers;
  }

  public async Task<(bool Ok, string? Error, CreateShippingInvoicePaymentRedirectResult? Result)> StartAsync(
    Guid userId,
    Guid fulfillmentGroupId,
    string provider,
    string successUrl,
    string cancelUrl,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == fulfillmentGroupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND", null);
    if (group.UserId != userId) return (false, "FORBIDDEN", null);

    if (!string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
      return (false, "BOX_NOT_CLOSED", null);

    var inv = await _db.ShippingInvoices
      .OrderByDescending(i => i.CreatedAt)
      .FirstOrDefaultAsync(i => i.FulfillmentGroupId == fulfillmentGroupId, ct);

    if (inv is null) return (false, "INVOICE_NOT_FOUND", null);

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID", null);

    if (inv.AmountCents <= 0)
      return (false, "NO_PAYMENT_REQUIRED", null);

    if (string.IsNullOrWhiteSpace(provider)) return (false, "PROVIDER_REQUIRED", null);
    if (string.IsNullOrWhiteSpace(successUrl) || string.IsNullOrWhiteSpace(cancelUrl))
      return (false, "RETURN_URL_REQUIRED", null);

    var p = _providers.FirstOrDefault(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    if (p is null) return (false, "UNSUPPORTED_PROVIDER", null);

    var redirect = await p.CreateRedirectAsync(inv.Id, fulfillmentGroupId, inv.AmountCents, inv.CurrencyCode, successUrl, cancelUrl, ct);

    inv.Provider = p.Provider;
    inv.ProviderCheckoutId = redirect.ProviderCheckoutId;
    inv.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return (true, null, redirect);
  }

  public async Task<(bool Ok, string? Error, ShippingInvoiceDetailDto? Detail)> GetInvoiceDetailForUserAsync(
  Guid userId,
  Guid invoiceId,
  CancellationToken ct)
  {
    var row = await (
      from inv in _db.ShippingInvoices.AsNoTracking()
      join grp in _db.FulfillmentGroups.AsNoTracking()
        on inv.FulfillmentGroupId equals grp.Id
      where inv.Id == invoiceId && grp.UserId == userId
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
        inv.CreatedAt,
        inv.UpdatedAt
      }
    ).SingleOrDefaultAsync(ct);

    if (row is null)
      return (false, "INVOICE_NOT_FOUND", null);

    var orderRows = await _db.Orders.AsNoTracking()
      .Where(o => o.UserId == userId && o.FulfillmentGroupId == row.FulfillmentGroupId)
      .OrderBy(o => o.CreatedAt)
      .Select(o => new
      {
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.CreatedAt
      })
      .ToListAsync(ct);

    var orderIds = orderRows.Select(x => x.Id).ToList();

    var itemRows = await (
      from line in _db.OrderLines.AsNoTracking()
      join listing in _db.Listings.AsNoTracking()
        on line.ListingId equals listing.Id
      join mineral in _db.Minerals.AsNoTracking()
        on listing.PrimaryMineralId equals mineral.Id into mineralJoin
      from mineral in mineralJoin.DefaultIfEmpty()
      where orderIds.Contains(line.OrderId)
      orderby line.CreatedAt
      select new
      {
        line.OrderId,
        line.Quantity,
        listing.Id,
        listing.Title,
        listing.LocalityDisplay,
        MineralName = mineral != null ? mineral.Name : null
      }
    ).ToListAsync(ct);

    var listingIds = itemRows.Select(x => x.Id).Distinct().ToList();

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

    var primaryImageByListingId = mediaRows
      .GroupBy(x => x.ListingId)
      .ToDictionary(g => g.Key, g => g.First().Url);

    var orderById = orderRows.ToDictionary(x => x.Id, x => x);

    var items = itemRows
      .Select(x =>
      {
        primaryImageByListingId.TryGetValue(x.Id, out var imageUrl);
        orderById.TryGetValue(x.OrderId, out var order);

        return new ShippingInvoiceDetailItemDto(
          OrderId: x.OrderId,
          OrderNumber: order?.OrderNumber,
          SourceType: order?.SourceType,
          ListingId: x.Id,
          ListingSlug: null,
          Title: x.Title,
          PrimaryImageUrl: imageUrl,
          MineralName: x.MineralName,
          Locality: x.LocalityDisplay,
          Quantity: x.Quantity
        );
      })
      .ToList();

    var relatedOrders = orderRows
      .Select(x => new ShippingInvoiceDetailRelatedOrderDto(
        x.Id,
        x.OrderNumber,
        x.SourceType))
      .ToList();

    var firstItem = items.FirstOrDefault();

    var itemCount = items.Sum(x => x.Quantity);

    var auctionOrderCount = relatedOrders.Count(x =>
      string.Equals(x.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase));

    var storeOrderCount = relatedOrders.Count(x =>
      string.Equals(x.SourceType, "STORE", StringComparison.OrdinalIgnoreCase));

    var detail = new ShippingInvoiceDetailDto(
      ShippingInvoiceId: row.Id,
      FulfillmentGroupId: row.FulfillmentGroupId,
      AmountCents: row.AmountCents,
      CurrencyCode: row.CurrencyCode,
      Status: row.Status,
      Provider: row.Provider,
      ProviderCheckoutId: row.ProviderCheckoutId,
      PaidAt: row.PaidAt,
      DueAt: null,
      CreatedAt: row.CreatedAt,
      UpdatedAt: row.UpdatedAt,
      ItemCount: itemCount,
      PreviewTitle: firstItem?.Title,
      PreviewImageUrl: firstItem?.PrimaryImageUrl,
      AuctionOrderCount: auctionOrderCount,
      StoreOrderCount: storeOrderCount,
      RelatedOrders: relatedOrders,
      Items: items
    );

    return (true, null, detail);
  }
}