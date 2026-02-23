using Microsoft.EntityFrameworkCore;
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
}