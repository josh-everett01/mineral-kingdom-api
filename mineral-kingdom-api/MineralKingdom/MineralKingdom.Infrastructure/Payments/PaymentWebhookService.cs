using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    Guid? holdId = null;
    Guid? paymentId = null;

    if (obj.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object)
    {
      if (md.TryGetProperty("hold_id", out var h) && Guid.TryParse(h.GetString(), out var hid))
        holdId = hid;

      if (md.TryGetProperty("payment_id", out var p) && Guid.TryParse(p.GetString(), out var pid))
        paymentId = pid;
    }

    if (holdId is null)
    {
      evt.ProcessedAt = now;
      await _db.SaveChangesAsync(ct);
      return;
    }

    // Update payment row if we can
    if (paymentId.HasValue)
    {
      var pay = await _db.CheckoutPayments.SingleOrDefaultAsync(x => x.Id == paymentId.Value, ct);
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

    // Source of truth: webhook triggers confirm paid
    _ = await _checkout.ConfirmPaidFromWebhookAsync(
      holdId.Value,
      paymentIntent ?? sessionId ?? eventId,
      now,
      ct);

    evt.ProcessedAt = now;
    await _db.SaveChangesAsync(ct);
  }

  public async Task ProcessPayPalAsync(string eventId, string payloadJson, DateTimeOffset now, CancellationToken ct)
  {
    var (isNew, evt) = await TryRecordEventAsync(PaymentProviders.PayPal, eventId, payloadJson, now, ct);
    if (!isNew) return;

    using var doc = JsonDocument.Parse(payloadJson);
    var root = doc.RootElement;

    var eventType = root.TryGetProperty("event_type", out var et) ? et.GetString() : null;
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
    var orderId = TryGetPayPalOrderId(res);

    CheckoutPayment? pay = null;

    if (!string.IsNullOrWhiteSpace(orderId))
    {
      pay = await _db.CheckoutPayments
        .SingleOrDefaultAsync(p => p.Provider == PaymentProviders.PayPal && p.ProviderCheckoutId == orderId, ct);
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
      captureId ?? orderId ?? eventId,
      now,
      ct);

    evt.ProcessedAt = now;
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
