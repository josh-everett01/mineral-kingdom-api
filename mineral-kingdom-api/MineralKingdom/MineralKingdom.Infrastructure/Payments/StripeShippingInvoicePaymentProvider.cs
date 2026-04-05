using Stripe.Checkout;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class StripeShippingInvoicePaymentProvider : IShippingInvoicePaymentProvider
{
  private readonly PaymentsOptions _payments;
  private readonly StripeOptions _stripe;

  public StripeShippingInvoicePaymentProvider(
    IOptions<PaymentsOptions> payments,
    IOptions<StripeOptions> stripe)
  {
    _payments = payments.Value;
    _stripe = stripe.Value;
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
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
    {
      var fakeSessionId = $"cs_test_ship_{shippingInvoiceId:N}";
      var fakeUrl = $"{successUrl}?session_id={fakeSessionId}";
      return new CreateShippingInvoicePaymentRedirectResult(fakeSessionId, fakeUrl);
    }

    if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
      throw new InvalidOperationException("STRIPE_NOT_CONFIGURED");

    Stripe.StripeConfiguration.ApiKey = _stripe.SecretKey;

    var service = new SessionService();
    var session = await service.CreateAsync(
      new SessionCreateOptions
      {
        Mode = "payment",
        SuccessUrl = successUrl,
        CancelUrl = cancelUrl,
        ClientReferenceId = shippingInvoiceId.ToString(),
        Metadata = new Dictionary<string, string>
        {
          ["shipping_invoice_id"] = shippingInvoiceId.ToString(),
          ["fulfillment_group_id"] = fulfillmentGroupId.ToString()
        },
        LineItems = new List<SessionLineItemOptions>
        {
          new()
          {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions
            {
              Currency = currencyCode.ToLowerInvariant(),
              UnitAmount = amountCents,
              ProductData = new SessionLineItemPriceDataProductDataOptions
              {
                Name = "Open Box shipping"
              }
            }
          }
        }
      },
      cancellationToken: ct);

    if (string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Url))
      throw new InvalidOperationException("STRIPE_SESSION_CREATION_FAILED");

    return new CreateShippingInvoicePaymentRedirectResult(session.Id, session.Url);
  }

  public Task<CaptureShippingInvoicePaymentResult> CaptureOrderAsync(
    string providerCheckoutId,
    CancellationToken ct)
  {
    throw new InvalidOperationException("PROVIDER_CAPTURE_NOT_SUPPORTED");
  }
}