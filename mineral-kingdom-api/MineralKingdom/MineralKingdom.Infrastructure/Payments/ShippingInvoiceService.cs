using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Payments.Realtime;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class ShippingInvoiceService
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingOptions _opts;
  private readonly EmailOutboxService _emails;
  private readonly IShippingInvoiceRealtimePublisher _realtime;

  public ShippingInvoiceService(
    MineralKingdomDbContext db,
    IOptions<ShippingOptions> opts,
    EmailOutboxService emails,
    IShippingInvoiceRealtimePublisher realtime)
  {
    _db = db;
    _opts = opts.Value ?? new ShippingOptions();
    _emails = emails;
    _realtime = realtime;
  }

  public async Task<(bool Ok, string? Error, ShippingInvoice? Invoice)> ResolveCurrentInvoiceForGroupAsync(
    Guid groupId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND", null);

    if (!string.Equals(group.BoxStatus, "LOCKED_FOR_REVIEW", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
      return (false, "BOX_NOT_READY_FOR_INVOICE", null);

    var invoices = await _db.ShippingInvoices
      .Where(i => i.FulfillmentGroupId == groupId)
      .OrderBy(i => i.CreatedAt)
      .ToListAsync(ct);

    var paid = invoices
      .Where(i => string.Equals(i.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      .ToList();

    var unpaid = invoices
      .Where(i => string.Equals(i.Status, "UNPAID", StringComparison.OrdinalIgnoreCase))
      .ToList();

    if (paid.Count > 1)
      return (false, "MULTIPLE_PAID_SHIPPING_INVOICES", null);

    if (paid.Count == 1)
    {
      var canonicalPaid = paid[0];

      var staleUnpaid = unpaid
        .Where(i => i.Id != canonicalPaid.Id)
        .ToList();

      if (staleUnpaid.Count > 0)
      {
        foreach (var stale in staleUnpaid)
        {
          stale.Status = "VOID";
          stale.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
      }

      return (true, null, canonicalPaid);
    }

    if (unpaid.Count > 1)
      return (false, "MULTIPLE_ACTIVE_SHIPPING_INVOICES", null);

    if (unpaid.Count == 1)
      return (true, null, unpaid[0]);

    var amount = await CalculateTierShippingCentsAsync(groupId, ct);
    var currency = string.IsNullOrWhiteSpace(_opts.CurrencyCode)
      ? "USD"
      : _opts.CurrencyCode.Trim().ToUpperInvariant();

    var inv = new ShippingInvoice
    {
      Id = Guid.NewGuid(),
      FulfillmentGroupId = groupId,

      CalculatedAmountCents = amount,
      AmountCents = amount,

      CurrencyCode = currency,
      Status = amount <= 0 ? "PAID" : "UNPAID",
      PaidAt = amount <= 0 ? now : null,

      Provider = null,
      ProviderCheckoutId = null,
      ProviderPaymentId = null,
      PaymentReference = null,

      IsOverride = false,
      OverrideReason = null,

      CreatedAt = now,
      UpdatedAt = now
    };

    _db.ShippingInvoices.Add(inv);

    if (amount <= 0 &&
        !string.Equals(group.ShipmentRequestStatus, ShipmentRequestStatuses.Paid, StringComparison.OrdinalIgnoreCase))
    {
      group.ShipmentRequestStatus = ShipmentRequestStatuses.Paid;
      group.UpdatedAt = now;
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

    try
    {
      if (group.UserId is Guid uid)
      {
        var toEmail = await _db.Users.AsNoTracking()
          .Where(u => u.Id == uid)
          .Select(u => u.Email)
          .SingleOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
          var payload =
            $"{{\"invoiceId\":\"{inv.Id}\",\"groupId\":\"{group.Id}\",\"amountCents\":{inv.AmountCents},\"currency\":\"{inv.CurrencyCode}\"}}";

          await _emails.EnqueueAsync(
            toEmail: toEmail,
            templateKey: EmailTemplateKeys.ShippingInvoiceCreated,
            payloadJson: payload,
            dedupeKey: EmailDedupeKeys.ShippingInvoiceCreated(inv.Id, toEmail),
            now: now,
            ct: ct);
        }
      }
    }
    catch
    {
      // best-effort
    }

    return (true, null, inv);
  }

  public async Task<(bool Ok, string? Error, ShippingInvoice? Invoice)> EnsureInvoiceForGroupAsync(
    Guid groupId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    return await ResolveCurrentInvoiceForGroupAsync(groupId, now, ct);
  }

  public async Task<long> CalculateTierShippingCentsAsync(Guid groupId, CancellationToken ct)
  {
    var merchTotal = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId == groupId)
      .SumAsync(o => (long)o.TotalCents, ct);

    var tiers = _opts.Tiers;
    if (tiers is null || tiers.Count == 0)
    {
      tiers = new List<ShippingTier>
      {
        new() { MinMerchTotalCents = 0,     MaxMerchTotalCents = 4999,  ShippingCents = 599 },
        new() { MinMerchTotalCents = 5000,  MaxMerchTotalCents = 9999,  ShippingCents = 899 },
        new() { MinMerchTotalCents = 10000, MaxMerchTotalCents = 19999, ShippingCents = 1299 },
        new() { MinMerchTotalCents = 20000, MaxMerchTotalCents = long.MaxValue, ShippingCents = 1999 },
      };
    }

    foreach (var t in tiers.OrderBy(t => t.MinMerchTotalCents))
    {
      if (merchTotal >= t.MinMerchTotalCents && merchTotal <= t.MaxMerchTotalCents)
        return t.ShippingCents;
    }

    return tiers.Last().ShippingCents;
  }

  public async Task<(bool Ok, string? Error)> AdminOverrideShippingAsync(
    Guid groupId,
    long amountCents,
    string? reason,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    if (amountCents < 0) return (false, "AMOUNT_NEGATIVE");

    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    var (ok, err, inv) = await EnsureInvoiceForGroupAsync(groupId, now, ct);
    if (!ok || inv is null) return (false, err);

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID");

    var beforeAmount = inv.AmountCents;

    inv.AmountCents = amountCents;
    inv.IsOverride = true;
    inv.OverrideReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    inv.UpdatedAt = now;

    if (amountCents == 0)
    {
      inv.Status = "PAID";
      inv.PaidAt = now;
      group.ShipmentRequestStatus = ShipmentRequestStatuses.Paid;
      group.UpdatedAt = now;
    }

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "SHIPPING_INVOICE_OVERRIDDEN",
      EntityType = "SHIPPING_INVOICE",
      EntityId = inv.Id,
      BeforeJson = $"{{\"amountCents\":{beforeAmount}}}",
      AfterJson = $"{{\"amountCents\":{amountCents},\"reason\":{(inv.OverrideReason is null ? "null" : $"\"{inv.OverrideReason.Replace("\"", "\\\"")}\"")}}}",
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);

    try
    {
      await _realtime.PublishInvoiceAsync(inv.Id, now, ct);
    }
    catch
    {
      // best-effort
    }

    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> MarkPaidFromWebhookAsync(
    Guid shippingInvoiceId,
    string provider,
    string providerPaymentId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var inv = await _db.ShippingInvoices.SingleOrDefaultAsync(i => i.Id == shippingInvoiceId, ct);
    if (inv is null) return (false, "INVOICE_NOT_FOUND");

    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == inv.FulfillmentGroupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    inv.Status = "PAID";
    inv.PaidAt = now;
    inv.Provider = provider;
    inv.ProviderPaymentId = providerPaymentId;
    inv.UpdatedAt = now;

    group.ShipmentRequestStatus = ShipmentRequestStatuses.Paid;
    group.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    var staleUnpaid = await _db.ShippingInvoices
      .Where(i =>
        i.FulfillmentGroupId == inv.FulfillmentGroupId &&
        i.Id != inv.Id &&
        i.Status == "UNPAID")
      .ToListAsync(ct);

    if (staleUnpaid.Count > 0)
    {
      foreach (var stale in staleUnpaid)
      {
        stale.Status = "VOID";
        stale.UpdatedAt = now;
      }

      await _db.SaveChangesAsync(ct);
    }

    try
    {
      await _realtime.PublishInvoiceAsync(inv.Id, now, ct);
    }
    catch
    {
      // best-effort
    }

    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> AdminOverrideInvoiceAsync(
    Guid invoiceId,
    long newAmountCents,
    string reason,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    if (newAmountCents < 0) return (false, "AMOUNT_INVALID");
    if (string.IsNullOrWhiteSpace(reason)) return (false, "REASON_REQUIRED");
    if (reason.Length > 500) return (false, "REASON_TOO_LONG");

    var inv = await _db.ShippingInvoices.SingleOrDefaultAsync(x => x.Id == invoiceId, ct);
    if (inv is null) return (false, "INVOICE_NOT_FOUND");

    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == inv.FulfillmentGroupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND");

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID");

    var beforeAmount = inv.AmountCents;
    var beforeReason = inv.OverrideReason;

    inv.AmountCents = newAmountCents;
    inv.IsOverride = true;
    inv.OverrideReason = reason.Trim();
    inv.UpdatedAt = now;

    if (newAmountCents == 0)
    {
      inv.Status = "PAID";
      inv.PaidAt = now;
      group.ShipmentRequestStatus = ShipmentRequestStatuses.Paid;
      group.UpdatedAt = now;
    }

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "SHIPPING_INVOICE_OVERRIDDEN",
      EntityType = "SHIPPING_INVOICE",
      EntityId = inv.Id,
      BeforeJson = $"{{\"amountCents\":{beforeAmount},\"reason\":{(beforeReason is null ? "null" : $"\"{beforeReason.Replace("\"", "\\\"")}\"")}}}",
      AfterJson = $"{{\"amountCents\":{newAmountCents},\"reason\":\"{reason.Trim().Replace("\"", "\\\"")}\"}}",
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);

    try
    {
      await _realtime.PublishInvoiceAsync(inv.Id, now, ct);
    }
    catch
    {
      // best-effort
    }

    return (true, null);
  }
}