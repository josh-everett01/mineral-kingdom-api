using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class PaymentWebhookService
{
  private readonly MineralKingdomDbContext _db;
  private readonly CheckoutService _checkout;

  public PaymentWebhookService(MineralKingdomDbContext db, CheckoutService checkout)
  {
    _db = db;
    _checkout = checkout;
  }

  // ----------------------------
  // STRIPE
  // ----------------------------
  public async Task ProcessStripeAsync(string eventId, string payloadJson, DateTimeOffset now, CancellationToken ct)
  {
    var (isNew, evt) = await TryRecordEventAsync(PaymentProviders.Stripe, eventId, payloadJson, now, ct);
    if (!isNew) return;

    using var doc = JsonDocument.Parse(payloadJson);
    var root = doc.RootElement;

    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
    if (!string.Equals(type, "checkout.session.completed", StringComparison.OrdinalIgnoreCase))
    {
      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    var obj = root.GetProperty("data").GetProperty("object");

    var sessionId = obj.TryGetProperty("id", out var sid) ? sid.GetString() : null;
    var paymentIntent = obj.TryGetProperty("payment_intent", out var pi) ? pi.GetString() : null;

    // Stripe session metadata can be either:
    // - checkout holds: hold_id + payment_id
    // - order payments: order_id + order_payment_id
    Guid? holdId = null;
    Guid? checkoutPaymentId = null;

    Guid? orderId = null;
    Guid? orderPaymentId = null;

    if (obj.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object)
    {
      if (md.TryGetProperty("hold_id", out var h) && Guid.TryParse(h.GetString(), out var hid))
        holdId = hid;

      if (md.TryGetProperty("payment_id", out var p) && Guid.TryParse(p.GetString(), out var cpid))
        checkoutPaymentId = cpid;

      if (md.TryGetProperty("order_id", out var o) && Guid.TryParse(o.GetString(), out var oid))
        orderId = oid;

      if (md.TryGetProperty("order_payment_id", out var op) && Guid.TryParse(op.GetString(), out var opid))
        orderPaymentId = opid;
    }

    // 1) STORE checkout hold flow (existing)
    if (holdId is not null)
    {
      if (checkoutPaymentId.HasValue)
      {
        var pay = await _db.CheckoutPayments.SingleOrDefaultAsync(x => x.Id == checkoutPaymentId.Value, ct);
        if (pay is not null)
        {
          pay.ProviderCheckoutId ??= sessionId;
          pay.ProviderPaymentId = paymentIntent;
          pay.Status = CheckoutPaymentStatuses.Succeeded;
          pay.UpdatedAt = now;
          evt.CheckoutPaymentId = pay.Id;
        }
      }
      else if (!string.IsNullOrWhiteSpace(sessionId))
      {
        var pay = await _db.CheckoutPayments
          .SingleOrDefaultAsync(x => x.Provider == PaymentProviders.Stripe && x.ProviderCheckoutId == sessionId, ct);
        if (pay is not null)
        {
          pay.ProviderPaymentId = paymentIntent;
          pay.Status = CheckoutPaymentStatuses.Succeeded;
          pay.UpdatedAt = now;
          evt.CheckoutPaymentId = pay.Id;
        }
      }

      _ = await _checkout.ConfirmPaidFromWebhookAsync(
        holdId.Value,
        paymentIntent ?? sessionId ?? eventId,
        now,
        ct);

      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    // 2) ORDER payment flow (AUCTION)
    if (orderPaymentId is not null || !string.IsNullOrWhiteSpace(sessionId))
    {
      OrderPayment? op = null;

      if (orderPaymentId.HasValue)
        op = await _db.OrderPayments.SingleOrDefaultAsync(x => x.Id == orderPaymentId.Value, ct);

      if (op is null && !string.IsNullOrWhiteSpace(sessionId))
        op = await _db.OrderPayments.SingleOrDefaultAsync(x => x.Provider == PaymentProviders.Stripe && x.ProviderCheckoutId == sessionId, ct);

      if (op is null)
      {
        evt.ProcessedAt = now;
        await _db.SaveChangesAsync(ct);
        return;
      }

      op.ProviderCheckoutId ??= sessionId;
      op.ProviderPaymentId = paymentIntent ?? op.ProviderPaymentId;
      op.Status = CheckoutPaymentStatuses.Succeeded; // same status strings
      op.UpdatedAt = now;
      evt.OrderPaymentId = op.Id;

      await ConfirmAuctionOrderPaidAsync(op.OrderId, paymentIntent ?? sessionId ?? eventId, now, ct);

      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    // If neither flow matched, just mark processed.
    evt.ProcessedAt = now;
    await _db.SaveChangesAsync(ct);
  }

  // ----------------------------
  // PAYPAL
  // ----------------------------
  public async Task ProcessPayPalAsync(string eventId, string payloadJson, DateTimeOffset now, CancellationToken ct)
  {
    var (isNew, evt) = await TryRecordEventAsync(PaymentProviders.PayPal, eventId, payloadJson, now, ct);
    if (!isNew) return;

    using var doc = JsonDocument.Parse(payloadJson);
    var root = doc.RootElement;

    var eventType = root.TryGetProperty("event_type", out var et) ? et.GetString() : null;

    // We only treat CAPTURE completed as paid
    if (!string.Equals(eventType, "PAYMENT.CAPTURE.COMPLETED", StringComparison.OrdinalIgnoreCase))
    {
      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    if (!root.TryGetProperty("resource", out var res) || res.ValueKind != JsonValueKind.Object)
    {
      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    var captureId = res.TryGetProperty("id", out var cid) ? cid.GetString() : null;

    // PayPal order id can appear in supplementary_data.related_ids.order_id
    var paypalOrderId = TryGetPayPalOrderId(res);

    // For your Orders v2 create request, you set:
    // - custom_id = OrderPaymentId
    // - invoice_id = OrderId
    Guid? orderPaymentId = null;
    Guid? orderId = null;

    if (res.TryGetProperty("custom_id", out var cust) && Guid.TryParse(cust.GetString(), out var opid))
      orderPaymentId = opid;

    if (res.TryGetProperty("invoice_id", out var inv) && Guid.TryParse(inv.GetString(), out var oid))
      orderId = oid;

    // ---- ORDER PAYMENTS (AUCTION) ----
    OrderPayment? op = null;

    if (orderPaymentId.HasValue)
      op = await _db.OrderPayments.SingleOrDefaultAsync(x => x.Id == orderPaymentId.Value, ct);

    if (op is null && !string.IsNullOrWhiteSpace(paypalOrderId))
      op = await _db.OrderPayments.SingleOrDefaultAsync(x => x.Provider == PaymentProviders.PayPal && x.ProviderCheckoutId == paypalOrderId, ct);

    if (op is not null)
    {
      op.ProviderCheckoutId ??= paypalOrderId;
      op.ProviderPaymentId = captureId ?? op.ProviderPaymentId;
      op.Status = CheckoutPaymentStatuses.Succeeded; // same status strings
      op.UpdatedAt = now;
      evt.OrderPaymentId = op.Id;

      await ConfirmAuctionOrderPaidAsync(op.OrderId, captureId ?? paypalOrderId ?? eventId, now, ct);

      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    // ---- CHECKOUT PAYMENTS (STORE HOLDS) ----
    // (Kept for backwards compatibility if you also use PayPal for checkout holds)
    CheckoutPayment? pay = null;

    if (!string.IsNullOrWhiteSpace(paypalOrderId))
    {
      pay = await _db.CheckoutPayments
        .SingleOrDefaultAsync(p => p.Provider == PaymentProviders.PayPal && p.ProviderCheckoutId == paypalOrderId, ct);
    }

    if (pay is null && !string.IsNullOrWhiteSpace(captureId))
    {
      pay = await _db.CheckoutPayments
        .SingleOrDefaultAsync(p => p.Provider == PaymentProviders.PayPal && p.ProviderPaymentId == captureId, ct);
    }

    if (pay is null)
    {
      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    pay.ProviderPaymentId = captureId ?? pay.ProviderPaymentId;
    pay.Status = CheckoutPaymentStatuses.Succeeded;
    pay.UpdatedAt = now;
    evt.CheckoutPaymentId = pay.Id;

    _ = await _checkout.ConfirmPaidFromWebhookAsync(
      pay.HoldId,
      captureId ?? paypalOrderId ?? eventId,
      now,
      ct);

    evt.ProcessedAt = now;
    await _db.SaveChangesAsync(ct);
  }

  // ----------------------------
  // Helpers
  // ----------------------------

  private async Task ConfirmAuctionOrderPaidAsync(Guid orderId, string paymentReference, DateTimeOffset now, CancellationToken ct)
  {
    // Idempotent: if already paid, do nothing
    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return;

    if (string.Equals(order.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return;

    // Mark order paid
    order.Status = "PAID";
    order.PaidAt ??= now;
    order.UpdatedAt = now;

    // Auction linkage: update auction + listing + offers
    if (string.Equals(order.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase) && order.AuctionId.HasValue)
    {
      var auction = await _db.Auctions.SingleOrDefaultAsync(a => a.Id == order.AuctionId.Value, ct);
      if (auction is not null)
      {
        if (string.Equals(auction.Status, AuctionStatuses.ClosedWaitingOnPayment, StringComparison.OrdinalIgnoreCase))
        {
          auction.Status = AuctionStatuses.ClosedPaid;
          auction.UpdatedAt = now;
        }

        // Mark listing SOLD + drain inventory
        var listing = await _db.Listings.SingleOrDefaultAsync(l => l.Id == auction.ListingId, ct);
        if (listing is not null)
        {
          listing.Status = MineralKingdom.Contracts.Listings.ListingStatuses.Sold;
          listing.QuantityAvailable = 0;
          listing.UpdatedAt = now;
        }

        // Disable store offers for that listing (belt-and-suspenders)
        var offers = await _db.StoreOffers
          .Where(o => o.ListingId == auction.ListingId && o.IsActive)
          .ToListAsync(ct);

        foreach (var o in offers)
        {
          o.IsActive = false;
          o.UpdatedAt = now;
        }
      }
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task<(bool IsNew, PaymentWebhookEvent Event)> TryRecordEventAsync(
    string provider,
    string eventId,
    string payloadJson,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var existing = await _db.PaymentWebhookEvents
      .SingleOrDefaultAsync(e => e.Provider == provider && e.EventId == eventId, ct);

    if (existing is not null)
      return (false, existing);

    var evt = new PaymentWebhookEvent
    {
      Id = Guid.NewGuid(),
      Provider = provider,
      EventId = eventId,
      PayloadJson = payloadJson,
      ReceivedAt = now
    };

    _db.PaymentWebhookEvents.Add(evt);
    await _db.SaveChangesAsync(ct);
    return (true, evt);
  }

  private static string? TryGetPayPalOrderId(JsonElement captureResource)
  {
    if (captureResource.TryGetProperty("supplementary_data", out var supp)
        && supp.ValueKind == JsonValueKind.Object
        && supp.TryGetProperty("related_ids", out var rel)
        && rel.ValueKind == JsonValueKind.Object
        && rel.TryGetProperty("order_id", out var oid))
    {
      return oid.GetString();
    }

    return null;
  }
}
