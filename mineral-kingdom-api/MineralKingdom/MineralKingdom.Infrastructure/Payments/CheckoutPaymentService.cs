using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Payments.Realtime;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class CheckoutPaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IReadOnlyList<ICheckoutPaymentProvider> _providers;
  private readonly PaymentsOptions _opts;
  private readonly ICheckoutPaymentRealtimePublisher _checkoutPaymentRealtimePublisher;

  public CheckoutPaymentService(
    MineralKingdomDbContext db,
    IEnumerable<ICheckoutPaymentProvider> providers,
    IOptions<PaymentsOptions> opts,
    ICheckoutPaymentRealtimePublisher checkoutPaymentRealtimePublisher)
  {
    _db = db;
    _providers = providers.ToList();
    _opts = opts.Value;
    _checkoutPaymentRealtimePublisher = checkoutPaymentRealtimePublisher;
  }

  public async Task<(bool Ok, string? Error, CheckoutPayment? Payment, string? RedirectUrl)> StartAsync(
  Guid holdId,
  string provider,
  string successUrl,
  string cancelUrl,
  string? shippingMode,
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

    var effectiveShippingMode = NormalizeStoreShippingMode(shippingMode, hold.UserId.HasValue);

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
      ShippingMode = effectiveShippingMode,
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.CheckoutPayments.Add(payment);

    await _db.SaveChangesAsync(ct);
    await _checkoutPaymentRealtimePublisher.PublishPaymentAsync(payment.Id, now, ct);

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
      return (false, "PROVIDER_NOT_CONFIGURED", payment, null);
    }

    payment.ProviderCheckoutId = redirect.ProviderCheckoutId;
    payment.Status = CheckoutPaymentStatuses.Redirected;
    payment.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);
    await _checkoutPaymentRealtimePublisher.PublishPaymentAsync(payment.Id, now, ct);

    return (true, null, payment, redirect.RedirectUrl);
  }

  private static string NormalizeStoreShippingMode(string? shippingMode, bool hasAuthenticatedHold)
  {
    var normalized = (shippingMode ?? "").Trim().ToUpperInvariant();

    if (!hasAuthenticatedHold)
      return StoreShippingModes.ShipNow;

    return normalized == StoreShippingModes.OpenBox
      ? StoreShippingModes.OpenBox
      : StoreShippingModes.ShipNow;
  }

  public async Task<(bool Ok, string? Error, CheckoutPayment? Payment)> CaptureAsync(
  Guid paymentId,
  DateTimeOffset now,
  CancellationToken ct)
  {
    var payment = await _db.CheckoutPayments.SingleOrDefaultAsync(p => p.Id == paymentId, ct);
    if (payment is null)
      return (false, "PAYMENT_NOT_FOUND", null);

    if (!string.Equals(payment.Provider, PaymentProviders.PayPal, StringComparison.OrdinalIgnoreCase))
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", payment);

    if (string.Equals(payment.Status, CheckoutPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
      return (true, null, payment);

    if (string.IsNullOrWhiteSpace(payment.ProviderCheckoutId))
      return (false, "PROVIDER_CHECKOUT_ID_MISSING", payment);

    if (string.Equals(_opts.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
    {
      payment.ProviderPaymentId ??= $"CAPTURE-FAKE-{payment.Id:N}";
      payment.Status = CheckoutPaymentStatuses.Succeeded;
      payment.UpdatedAt = now;

      await _db.SaveChangesAsync(ct);
      await _checkoutPaymentRealtimePublisher.PublishPaymentAsync(payment.Id, now, ct);

      return (true, null, payment);
    }

    var paypalProvider = _providers.OfType<PayPalCheckoutPaymentProvider>().SingleOrDefault();
    if (paypalProvider is null)
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", payment);

    try
    {
      var capture = await paypalProvider.CaptureOrderAsync(payment.ProviderCheckoutId, ct);

      payment.ProviderCheckoutId = capture.ProviderCheckoutId;
      payment.UpdatedAt = now;

      if (!string.IsNullOrWhiteSpace(capture.CaptureId))
      {
        payment.ProviderPaymentId = capture.CaptureId;
      }

      if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(capture.Status, "ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase))
      {
        payment.Status = CheckoutPaymentStatuses.Succeeded;
      }

      await _db.SaveChangesAsync(ct);
      await _checkoutPaymentRealtimePublisher.PublishPaymentAsync(payment.Id, now, ct);

      return (true, null, payment);
    }
    catch (InvalidOperationException ex) when (
      ex.Message.StartsWith("PAYPAL_NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_CAPTURE_ORDER_FAILED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_", StringComparison.OrdinalIgnoreCase))
    {
      payment.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);

      try
      {
        await _checkoutPaymentRealtimePublisher.PublishPaymentAsync(payment.Id, now, ct);
      }
      catch
      {
        // best-effort
      }

      return (false, "PAYPAL_CAPTURE_FAILED", payment);
    }
  }

  public async Task<CheckoutPayment?> GetAsync(Guid id, CancellationToken ct)
    => await _db.CheckoutPayments.SingleOrDefaultAsync(p => p.Id == id, ct);

  public async Task<PaymentConfirmationResponse?> GetConfirmationAsync(Guid id, CancellationToken ct)
  {
    var payment = await _db.CheckoutPayments
      .AsNoTracking()
      .SingleOrDefaultAsync(p => p.Id == id, ct);

    if (payment is null)
      return null;

    var order = await _db.Orders
      .AsNoTracking()
      .SingleOrDefaultAsync(o => o.CheckoutHoldId == payment.HoldId, ct);

    return new PaymentConfirmationResponse(
      PaymentId: payment.Id,
      Provider: payment.Provider,
      PaymentStatus: payment.Status,
      IsConfirmed: order is not null,
      OrderId: order?.Id,
      OrderNumber: order?.OrderNumber,
      OrderStatus: order?.Status,
      OrderTotalCents: order?.TotalCents,
      OrderCurrencyCode: order?.CurrencyCode,
      GuestEmail: order?.GuestEmail
    );
  }

  private ICheckoutPaymentProvider? ResolveProvider(string requestedProvider)
  {
    if ((_opts.Mode ?? "").Trim().Equals("FAKE", StringComparison.OrdinalIgnoreCase))
      return _providers.SingleOrDefault(p => p.Provider == PaymentProviders.Fake);

    return _providers.SingleOrDefault(p => p.Provider.Equals(requestedProvider, StringComparison.OrdinalIgnoreCase));
  }
}