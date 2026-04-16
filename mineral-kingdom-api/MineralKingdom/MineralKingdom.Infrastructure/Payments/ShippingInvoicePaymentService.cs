using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Payments.Realtime;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class ShippingInvoicePaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingInvoiceService _shippingInvoices;
  private readonly IEnumerable<IShippingInvoicePaymentProvider> _providers;
  private readonly EmailOutboxService _emails;
  private readonly IShippingInvoiceRealtimePublisher _realtime;

  public ShippingInvoicePaymentService(
    MineralKingdomDbContext db,
    ShippingInvoiceService shippingInvoices,
    IEnumerable<IShippingInvoicePaymentProvider> providers,
    EmailOutboxService emails,
    IShippingInvoiceRealtimePublisher realtime)
  {
    _db = db;
    _shippingInvoices = shippingInvoices;
    _providers = providers;
    _emails = emails;
    _realtime = realtime;
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

    if (!string.Equals(group.BoxStatus, "LOCKED_FOR_REVIEW", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
      return (false, "BOX_NOT_READY_FOR_PAYMENT", null);

    var (resolvedOk, resolvedErr, inv) =
      await _shippingInvoices.ResolveCurrentInvoiceForGroupAsync(fulfillmentGroupId, now, ct);

    if (!resolvedOk || inv is null)
      return (false, resolvedErr ?? "INVOICE_NOT_FOUND", null);

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID", null);

    if (!string.Equals(inv.Status, "UNPAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_NOT_ACTIONABLE", null);

    if (inv.AmountCents <= 0)
      return (false, "NO_PAYMENT_REQUIRED", null);

    if (string.IsNullOrWhiteSpace(provider)) return (false, "PROVIDER_REQUIRED", null);
    if (string.IsNullOrWhiteSpace(successUrl) || string.IsNullOrWhiteSpace(cancelUrl))
      return (false, "RETURN_URL_REQUIRED", null);

    var p = _providers.FirstOrDefault(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    if (p is null) return (false, "UNSUPPORTED_PROVIDER", null);

    var redirect = await p.CreateRedirectAsync(
      inv.Id,
      fulfillmentGroupId,
      inv.AmountCents,
      inv.CurrencyCode,
      successUrl,
      cancelUrl,
      ct);

    inv.Provider = p.Provider;
    inv.ProviderCheckoutId = redirect.ProviderCheckoutId;
    inv.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return (true, null, redirect);
  }

  public async Task<(bool Ok, string? Error, ShippingInvoice? Invoice)> CaptureAsync(
    Guid invoiceId,
    Guid userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var inv = await _db.ShippingInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId, ct);
    if (inv is null)
      return (false, "INVOICE_NOT_FOUND", null);

    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == inv.FulfillmentGroupId, ct);
    if (group is null || group.UserId != userId)
      return (false, "INVOICE_NOT_FOUND", null);

    if (!string.Equals(inv.Provider, PaymentProviders.PayPal, StringComparison.OrdinalIgnoreCase))
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", inv);

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (true, null, inv);

    if (string.IsNullOrWhiteSpace(inv.ProviderCheckoutId))
      return (false, "PROVIDER_CHECKOUT_ID_MISSING", inv);

    var paypalProvider = _providers.OfType<PayPalShippingInvoicePaymentProvider>().SingleOrDefault();
    if (paypalProvider is null)
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", inv);

    try
    {
      var capture = await paypalProvider.CaptureOrderAsync(inv.ProviderCheckoutId, ct);

      inv.ProviderCheckoutId = capture.ProviderCheckoutId;
      inv.UpdatedAt = now;

      if (!string.IsNullOrWhiteSpace(capture.CaptureId))
      {
        inv.ProviderPaymentId = capture.CaptureId;
      }

      var isCaptureConfirmed =
        string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(capture.Status, "ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase);

      if (!isCaptureConfirmed)
        return (false, "PAYPAL_CAPTURE_FAILED", inv);

      var wasPaid = string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase);

      inv.Status = "PAID";
      inv.PaidAt ??= now;
      inv.UpdatedAt = now;

      if (!string.Equals(group.ShipmentRequestStatus, ShipmentRequestStatuses.Paid, StringComparison.OrdinalIgnoreCase))
      {
        group.ShipmentRequestStatus = ShipmentRequestStatuses.Paid;
        group.UpdatedAt = now;
      }

      if (!wasPaid)
      {
        try
        {
          if (group.UserId is Guid uid)
          {
            var toEmail = await _db.Users
              .AsNoTracking()
              .Where(u => u.Id == uid)
              .Select(u => u.Email)
              .SingleOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(toEmail))
            {
              var payload =
                $"{{\"invoiceId\":\"{inv.Id}\",\"groupId\":\"{inv.FulfillmentGroupId}\",\"amountCents\":{inv.AmountCents}}}";

              await _emails.EnqueueAsync(
                toEmail: toEmail,
                templateKey: EmailTemplateKeys.ShippingInvoicePaid,
                payloadJson: payload,
                dedupeKey: EmailDedupeKeys.ShippingInvoicePaid(inv.Id, toEmail),
                now: now,
                ct: ct);
            }
          }
        }
        catch
        {
          // best-effort
        }
      }

      await _db.SaveChangesAsync(ct);

      try
      {
        await _realtime.PublishInvoiceAsync(inv.Id, now, ct);
      }
      catch
      {
        // best-effort
      }

      return (true, null, inv);
    }
    catch (InvalidOperationException ex) when (
      ex.Message.StartsWith("PAYPAL_NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_CAPTURE_ORDER_FAILED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_", StringComparison.OrdinalIgnoreCase))
    {
      inv.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
      return (false, "PAYPAL_CAPTURE_FAILED", inv);
    }
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

    var requiresShippingInvoice = await _db.Orders.AsNoTracking()
      .AnyAsync(o =>
        o.UserId == userId &&
        o.FulfillmentGroupId == row.FulfillmentGroupId &&
        o.ShippingMode == StoreShippingModes.OpenBox,
        ct);

    if (!requiresShippingInvoice)
      return (false, "INVOICE_NOT_FOUND", null);

    var orderRows = await _db.Orders.AsNoTracking()
      .Where(o =>
        o.UserId == userId &&
        o.FulfillmentGroupId == row.FulfillmentGroupId &&
        o.ShippingMode == StoreShippingModes.OpenBox)
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

  public async Task<(bool Ok, string? Error, CreateShippingInvoicePaymentRedirectResult? Result)> StartForInvoiceAsync(
  Guid invoiceId,
  Guid userId,
  string provider,
  string successUrl,
  string cancelUrl,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var invoice = await _db.ShippingInvoices
      .SingleOrDefaultAsync(i => i.Id == invoiceId, ct);

    if (invoice is null)
      return (false, "INVOICE_NOT_FOUND", null);

    var group = await _db.FulfillmentGroups
      .AsNoTracking()
      .SingleOrDefaultAsync(g => g.Id == invoice.FulfillmentGroupId, ct);

    if (group is null)
      return (false, "INVOICE_NOT_FOUND", null);

    if (group.UserId != userId)
      return (false, "FORBIDDEN", null);

    var requiresShippingInvoice = await _db.Orders.AsNoTracking()
      .AnyAsync(o =>
        o.UserId == userId &&
        o.FulfillmentGroupId == invoice.FulfillmentGroupId &&
        o.ShippingMode == StoreShippingModes.OpenBox,
        ct);

    if (!requiresShippingInvoice)
      return (false, "INVOICE_NOT_FOUND", null);

    if (string.Equals(invoice.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID", null);

    if (!string.Equals(invoice.Status, "UNPAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVALID_INVOICE_STATUS", null);

    return await StartAsync(
      userId,
      invoice.FulfillmentGroupId,
      provider,
      successUrl,
      cancelUrl,
      now,
      ct);
  }
}