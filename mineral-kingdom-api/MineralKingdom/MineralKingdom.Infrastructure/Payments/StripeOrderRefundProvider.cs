using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using Stripe;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class StripeOrderRefundProvider : IOrderRefundProvider
{
  private readonly MineralKingdomDbContext _db;
  private readonly PaymentsOptions _payments;
  private readonly StripeOptions _stripe;

  public StripeOrderRefundProvider(
    MineralKingdomDbContext db,
    IOptions<PaymentsOptions> payments,
    IOptions<StripeOptions> stripe)
  {
    _db = db;
    _payments = payments.Value;
    _stripe = stripe.Value;
  }

  public string Provider => PaymentProviders.Stripe;

  public async Task<CreateRefundResult> RefundAsync(
    Guid orderId,
    long amountCents,
    string currencyCode,
    string reason,
    CancellationToken ct)
  {
    // FAKE mode: deterministic refund id
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
      return new CreateRefundResult($"re_fake_{orderId:N}_{amountCents}");

    if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
      throw new InvalidOperationException("STRIPE_NOT_CONFIGURED");

    // Find latest paid Stripe payment for this order
    var pay = await _db.OrderPayments.AsNoTracking()
      .Where(p =>
        p.OrderId == orderId &&
        p.Provider == PaymentProviders.Stripe &&
        p.Status == CheckoutPaymentStatuses.Succeeded)
      .OrderByDescending(p => p.CreatedAt)
      .FirstOrDefaultAsync(ct);

    if (pay is null)
      throw new InvalidOperationException("STRIPE_PAYMENT_NOT_FOUND");

    // Prefer ProviderPaymentId (PaymentIntent/Charge id), fallback to checkout id
    var paymentRef = pay.ProviderPaymentId ?? pay.ProviderCheckoutId;
    if (string.IsNullOrWhiteSpace(paymentRef))
      throw new InvalidOperationException("STRIPE_PAYMENT_REFERENCE_MISSING");

    StripeConfiguration.ApiKey = _stripe.SecretKey;

    var refunds = new RefundService();

    var opts = new RefundCreateOptions
    {
      Amount = amountCents,
      // Stripe reasons are limited; metadata carries our real reason.
      Reason = "requested_by_customer",
      Metadata = new Dictionary<string, string>
      {
        ["orderId"] = orderId.ToString(),
        ["reason"] = reason
      }
    };

    // Support common ids
    if (paymentRef.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
      opts.PaymentIntent = paymentRef;
    else if (paymentRef.StartsWith("ch_", StringComparison.OrdinalIgnoreCase))
      opts.Charge = paymentRef;
    else
      opts.PaymentIntent = paymentRef; // best-effort default

    var refund = await refunds.CreateAsync(opts, cancellationToken: ct);

    if (string.IsNullOrWhiteSpace(refund?.Id))
      throw new InvalidOperationException("STRIPE_REFUND_FAILED");

    return new CreateRefundResult(refund.Id);
  }
}