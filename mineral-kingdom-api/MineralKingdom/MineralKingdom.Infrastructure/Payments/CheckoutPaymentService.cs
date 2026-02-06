using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class CheckoutPaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IReadOnlyList<ICheckoutPaymentProvider> _providers;
  private readonly PaymentsOptions _opts;

  public CheckoutPaymentService(
    MineralKingdomDbContext db,
    IEnumerable<ICheckoutPaymentProvider> providers,
    IOptions<PaymentsOptions> opts)
  {
    _db = db;
    _providers = providers.ToList();
    _opts = opts.Value;
  }

  public async Task<(bool Ok, string? Error, CheckoutPayment? Payment, string? RedirectUrl)> StartAsync(
    Guid holdId,
    string provider,
    string successUrl,
    string cancelUrl,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var hold = await _db.CheckoutHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
    if (hold is null) return (false, "HOLD_NOT_FOUND", null, null);

    if (hold.Status != CheckoutHoldStatuses.Active)
      return (false, "HOLD_NOT_ACTIVE", null, null);

    if (now > hold.ExpiresAt)
      return (false, "HOLD_EXPIRED", null, null);

    var cart = await _db.Carts
      .Include(c => c.Lines)
      .SingleAsync(c => c.Id == hold.CartId, ct);

    if (cart.Status != CartStatuses.Active)
      return (false, "CART_NOT_ACTIVE", null, null);

    if (cart.Lines.Count == 0)
      return (false, "CART_EMPTY", null, null);

    // Load offers for pricing snapshot
    var offerIds = cart.Lines.Select(l => l.OfferId).ToList();
    var offers = await _db.StoreOffers
  .Where(o => offerIds.Contains(o.Id))
  .ToListAsync(ct);


    if (offers.Count != offerIds.Count)
      return (false, "OFFER_NOT_FOUND", null, null);

    long total = 0;
    var lineItems = new List<CheckoutLineItem>();

    foreach (var line in cart.Lines)
    {
      var offer = offers.Single(o => o.Id == line.OfferId);
      var unit = (long)offer.PriceCents;
      var qty = (long)line.Quantity;
      total += unit * qty;

      lineItems.Add(new CheckoutLineItem(
        Name: "Item",
        UnitAmountCents: unit,
        Quantity: qty
      ));
    }

    var payment = new CheckoutPayment
    {
      Id = Guid.NewGuid(),
      HoldId = hold.Id,
      CartId = hold.CartId,
      Provider = provider,
      Status = CheckoutPaymentStatuses.Created,
      AmountCents = checked((int)total),
      CurrencyCode = "USD",
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.CheckoutPayments.Add(payment);
    await _db.SaveChangesAsync(ct);

    var impl = ResolveProvider(provider);
    if (impl is null)
      return (false, "PROVIDER_NOT_SUPPORTED", null, null);

    CreatePaymentRedirectResult redirect;

    try
    {
      redirect = await impl.CreateRedirectAsync(
        new CreatePaymentRedirectRequest(
          HoldId: hold.Id,
          PaymentId: payment.Id,
          CurrencyCode: payment.CurrencyCode,
          AmountCents: payment.AmountCents,
          LineItems: lineItems,
          SuccessUrl: successUrl,
          CancelUrl: cancelUrl
        ),
        ct);
    }
    catch (InvalidOperationException ex) when (
      ex.Message.StartsWith("PAYPAL_NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_", StringComparison.OrdinalIgnoreCase))
    {
      // keep the payment row (Created), but don’t blow up the request
      return (false, "PROVIDER_NOT_CONFIGURED", payment, null);
    }

    payment.ProviderCheckoutId = redirect.ProviderCheckoutId;
    payment.Status = CheckoutPaymentStatuses.Redirected;
    payment.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return (true, null, payment, redirect.RedirectUrl);
  }

  public async Task<CheckoutPayment?> GetAsync(Guid id, CancellationToken ct)
    => await _db.CheckoutPayments.SingleOrDefaultAsync(p => p.Id == id, ct);

  private ICheckoutPaymentProvider? ResolveProvider(string requestedProvider)
  {
    if ((_opts.Mode ?? "").Trim().Equals("FAKE", StringComparison.OrdinalIgnoreCase))
      return _providers.SingleOrDefault(p => p.Provider == PaymentProviders.Fake);

    return _providers.SingleOrDefault(p => p.Provider.Equals(requestedProvider, StringComparison.OrdinalIgnoreCase));
  }
}
