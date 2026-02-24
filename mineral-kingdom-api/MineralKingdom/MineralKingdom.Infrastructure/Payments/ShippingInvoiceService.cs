using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class ShippingInvoiceService
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingOptions _opts;

  private readonly EmailOutboxService _emails;

  public ShippingInvoiceService(MineralKingdomDbContext db, IOptions<ShippingOptions> opts, EmailOutboxService emails)
  {
    _db = db;
    _opts = opts.Value ?? new ShippingOptions();
    _emails = emails;
  }

  public async Task<(bool Ok, string? Error, ShippingInvoice? Invoice)> EnsureInvoiceForGroupAsync(
    Guid groupId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var group = await _db.FulfillmentGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
    if (group is null) return (false, "GROUP_NOT_FOUND", null);

    // Only generate once the box is closed (Open Box behavior)
    if (!string.Equals(group.BoxStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
      return (false, "BOX_NOT_CLOSED", null);

    // If an active unpaid invoice exists, return it (idempotent)
    var existing = await _db.ShippingInvoices
      .OrderByDescending(i => i.CreatedAt)
      .FirstOrDefaultAsync(i => i.FulfillmentGroupId == groupId && i.Status == "UNPAID", ct);

    if (existing is not null) return (true, null, existing);

    // If paid invoice exists, no need to create another
    var alreadyPaid = await _db.ShippingInvoices
      .AnyAsync(i => i.FulfillmentGroupId == groupId && i.Status == "PAID", ct);

    if (alreadyPaid)
    {
      var latest = await _db.ShippingInvoices
        .OrderByDescending(i => i.CreatedAt)
        .FirstAsync(i => i.FulfillmentGroupId == groupId, ct);

      return (true, null, latest);
    }

    var amount = await CalculateTierShippingCentsAsync(groupId, ct);
    var currency = string.IsNullOrWhiteSpace(_opts.CurrencyCode) ? "USD" : _opts.CurrencyCode.Trim().ToUpperInvariant();

    var inv = new ShippingInvoice
    {
      Id = Guid.NewGuid(),
      FulfillmentGroupId = groupId,

      AmountCents = amount,
      CurrencyCode = currency,
      Status = amount <= 0 ? "PAID" : "UNPAID",  // amount 0 => treat as satisfied
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
    await _db.SaveChangesAsync(ct);

    // Enqueue SHIPPING_INVOICE_CREATED (best-effort; dedupe prevents duplicates)
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
          var payload = $"{{\"invoiceId\":\"{inv.Id}\",\"groupId\":\"{group.Id}\",\"amountCents\":{inv.AmountCents},\"currency\":\"{inv.CurrencyCode}\"}}";
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
    catch { /* best-effort */ }

    return (true, null, inv);
  }

  public async Task<long> CalculateTierShippingCentsAsync(Guid groupId, CancellationToken ct)
  {
    // total merchandise value = sum(order.TotalCents) for orders in group
    var merchTotal = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId == groupId)
      .SumAsync(o => (long)o.TotalCents, ct);

    // Fallback tiers if config not present
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

    // defensive fallback
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

    // Ensure a single "active" invoice we can override: unpaid if exists else create
    var (ok, err, inv) = await EnsureInvoiceForGroupAsync(groupId, now, ct);
    if (!ok || inv is null) return (false, err);

    // If already paid and admin wants to change amount, disallow in v1
    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (false, "INVOICE_ALREADY_PAID");

    var beforeAmount = inv.AmountCents;

    inv.AmountCents = amountCents;
    inv.IsOverride = true;
    inv.OverrideReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    inv.UpdatedAt = now;

    // If override to 0, mark as paid/satisfied
    if (amountCents == 0)
    {
      inv.Status = "PAID";
      inv.PaidAt = now;
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

    if (string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase))
      return (true, null);

    inv.Status = "PAID";
    inv.PaidAt = now;
    inv.Provider = provider;
    inv.ProviderPaymentId = providerPaymentId;
    inv.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }
}