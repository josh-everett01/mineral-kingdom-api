using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class OrderRefundService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IEnumerable<IOrderRefundProvider> _providers;
  private readonly EmailOutboxService _emails;
  private readonly UserNotificationPreferencesService _prefs;

  public OrderRefundService(
    MineralKingdomDbContext db,
    IEnumerable<IOrderRefundProvider> providers,
    EmailOutboxService emails,
    UserNotificationPreferencesService prefs)
  {
    _db = db;
    _providers = providers;
    _emails = emails;
    _prefs = prefs;
  }

  public async Task<(bool Ok, string? Error, OrderRefund? Refund)> AdminCreateRefundAsync(
    Guid orderId,
    long amountCents,
    string reason,
    string provider,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    if (amountCents <= 0) return (false, "AMOUNT_REQUIRED", null);
    if (amountCents > 100_000_000_000L) return (false, "AMOUNT_TOO_LARGE", null);

    if (string.IsNullOrWhiteSpace(reason)) return (false, "REASON_REQUIRED", null);
    reason = reason.Trim();
    if (reason.Length > 500) return (false, "REASON_TOO_LONG", null);

    if (string.IsNullOrWhiteSpace(provider)) return (false, "PROVIDER_REQUIRED", null);
    provider = provider.Trim().ToUpperInvariant();

    var refundProvider = _providers.FirstOrDefault(x =>
      string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));

    if (refundProvider is null) return (false, "PROVIDER_NOT_SUPPORTED", null);

    string? notificationEmail = null;
    Guid? notificationUserId = null;
    string? orderNumberForEmail = null;
    string currencyCodeForEmail = "USD";
    long refundedSoFarAfter = 0L;
    long remainingAfter = 0L;
    bool isFullRefund = false;

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var order = await _db.Orders
      .FromSqlInterpolated($@"SELECT * FROM orders WHERE ""Id"" = {orderId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (order is null) return (false, "ORDER_NOT_FOUND", null);

    var normalizedSucceededStatus = CheckoutPaymentStatuses.Succeeded.ToUpperInvariant();

    var hasSucceededPayment = await _db.OrderPayments.AsNoTracking()
      .AnyAsync(op =>
        op.OrderId == order.Id &&
        op.Provider.ToUpper() == provider &&
        op.Status.ToUpper() == normalizedSucceededStatus,
        ct);

    if (!hasSucceededPayment)
      return (false, "ORDER_NOT_REFUNDABLE", null);

    var refundedSoFar = await _db.OrderRefunds.AsNoTracking()
      .Where(r => r.OrderId == orderId)
      .SumAsync(r => (long?)r.AmountCents, ct) ?? 0L;

    var remaining = (long)order.TotalCents - refundedSoFar;

    if (remaining <= 0) return (false, "ORDER_ALREADY_REFUNDED", null);
    if (amountCents > remaining) return (false, "REFUND_EXCEEDS_REMAINING", null);

    CreateRefundResult result;

    try
    {
      result = await refundProvider.RefundAsync(orderId, amountCents, order.CurrencyCode, reason, ct);
    }
    catch (InvalidOperationException ex) when (
      ex.Message == "PAYPAL_PAYMENT_NOT_FOUND" ||
      ex.Message == "STRIPE_PAYMENT_NOT_FOUND")
    {
      return (false, "ORDER_NOT_REFUNDABLE", null);
    }

    var refund = new OrderRefund
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      Provider = provider,
      ProviderRefundId = result.ProviderRefundId,
      AmountCents = amountCents,
      CurrencyCode = order.CurrencyCode,
      Reason = reason,
      CreatedAt = now
    };

    _db.OrderRefunds.Add(refund);

    var action = amountCents == remaining ? "ORDER_REFUNDED_FULL" : "ORDER_REFUNDED_PARTIAL";

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = action,
      EntityType = "ORDER",
      EntityId = orderId,
      BeforeJson = $"{{\"refundedSoFar\":{refundedSoFar},\"remaining\":{remaining}}}",
      AfterJson = $"{{\"refundId\":\"{refund.Id}\",\"amountCents\":{amountCents},\"provider\":\"{provider}\",\"providerRefundId\":{System.Text.Json.JsonSerializer.Serialize(refund.ProviderRefundId)},\"reason\":{System.Text.Json.JsonSerializer.Serialize(reason)}}}",
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    // Capture notification context before commit
    notificationUserId = order.UserId;
    notificationEmail = order.GuestEmail;
    orderNumberForEmail = order.OrderNumber;
    currencyCodeForEmail = order.CurrencyCode;
    refundedSoFarAfter = refundedSoFar + amountCents;
    remainingAfter = Math.Max(0L, remaining - amountCents);
    isFullRefund = amountCents == remaining;

    if (order.UserId.HasValue)
    {
      notificationEmail = await _db.Users.AsNoTracking()
        .Where(u => u.Id == order.UserId.Value)
        .Select(u => u.Email)
        .SingleOrDefaultAsync(ct);
    }

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    try
    {
      if (!string.IsNullOrWhiteSpace(notificationEmail))
      {
        var shouldSend = true;

        if (notificationUserId.HasValue)
        {
          var prefs = await _prefs.GetOrCreateAsync(notificationUserId.Value, now, ct);
          shouldSend = UserNotificationPreferencesService.IsEnabled(
            prefs,
            OptionalEmailKeys.OrderRefund);
        }

        if (shouldSend)
        {
          var templateKey = isFullRefund
            ? EmailTemplateKeys.OrderRefunded
            : EmailTemplateKeys.OrderRefundedPartial;

          var payloadJson =
            $$"""
            {
              "orderId":"{{orderId}}",
              "orderNumber":{{System.Text.Json.JsonSerializer.Serialize(orderNumberForEmail)}},
              "amountCents":{{amountCents}},
              "currencyCode":{{System.Text.Json.JsonSerializer.Serialize(currencyCodeForEmail)}},
              "provider":{{System.Text.Json.JsonSerializer.Serialize(provider)}},
              "reason":{{System.Text.Json.JsonSerializer.Serialize(reason)}},
              "totalRefundedCents":{{refundedSoFarAfter}},
              "remainingRefundableCents":{{remainingAfter}},
              "isFullRefund":{{(isFullRefund ? "true" : "false")}}
            }
            """;

          await _emails.EnqueueAsync(
            toEmail: notificationEmail,
            templateKey: templateKey,
            payloadJson: payloadJson,
            dedupeKey: $"refund:{orderId}:{refund.Id}:{notificationEmail}",
            now: now,
            ct: ct);
        }
      }
    }
    catch
    {
      // do not fail refund if notification enqueue fails
    }

    return (true, null, refund);
  }
}