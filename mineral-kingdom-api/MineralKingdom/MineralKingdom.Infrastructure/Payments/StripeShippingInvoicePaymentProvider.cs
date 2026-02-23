using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using Stripe;
using Stripe.Checkout;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class StripeShippingInvoicePaymentProvider : IShippingInvoicePaymentProvider
{
  private readonly StripeOptions _stripe;
  private readonly PaymentsOptions _payments;

  public StripeShippingInvoicePaymentProvider(IOptions<StripeOptions> stripe, IOptions<PaymentsOptions> payments)
  {
    _stripe = stripe.Value;
    _payments = payments.Value;
  }

  public string Provider => PaymentProviders.Stripe;

  public async Task<CreateShippingInvoicePaymentRedirectResult> CreateRedirectAsync(
    Guid shippingInvoiceId,
    Guid fulfillmentGroupId,
    long amountCents,
    string currencyCode,
    string successUrl,
    string cancelUrl,
    CancellationToken ct)
  {
    // FAKE mode: deterministic redirect for tests/dev without calling Stripe
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
    {
      var fakeSessionId = $"cs_test_{shippingInvoiceId:N}";
      var fakeUrl = $"https://example.invalid/stripe/checkout?session_id={fakeSessionId}";
      return new CreateShippingInvoicePaymentRedirectResult(fakeSessionId, fakeUrl);
    }

    if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
      throw new InvalidOperationException("Stripe SecretKey is not configured (MK_STRIPE__SECRET_KEY).");

    StripeConfiguration.ApiKey = _stripe.SecretKey;

    var svc = new SessionService();

    var options = new SessionCreateOptions
    {
      Mode = "payment",
      SuccessUrl = successUrl,
      CancelUrl = cancelUrl,
      Metadata = new Dictionary<string, string>
      {
        ["shipping_invoice_id"] = shippingInvoiceId.ToString(),
        ["fulfillment_group_id"] = fulfillmentGroupId.ToString()
      },
      LineItems = new List<SessionLineItemOptions>
      {
        new SessionLineItemOptions
        {
          Quantity = 1,
          PriceData = new SessionLineItemPriceDataOptions
          {
            Currency = currencyCode.ToLowerInvariant(),
            UnitAmount = amountCents,
            ProductData = new SessionLineItemPriceDataProductDataOptions
            {
              Name = "Shipping"
            }
          }
        }
      }
    };

    var session = await svc.CreateAsync(options, cancellationToken: ct);

    return new CreateShippingInvoicePaymentRedirectResult(
      ProviderCheckoutId: session.Id,
      RedirectUrl: session.Url);
  }
}