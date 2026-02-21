using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using Stripe;
using Stripe.Checkout;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class StripeOrderPaymentProvider : IOrderPaymentProvider
{
  private readonly StripeOptions _opts;

  public StripeOrderPaymentProvider(IOptions<StripeOptions> opts)
  {
    _opts = opts.Value;
  }

  public string Provider => PaymentProviders.Stripe;

  public async Task<CreateOrderPaymentRedirectResult> CreateRedirectAsync(CreateOrderPaymentRedirectRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(_opts.SecretKey))
      throw new InvalidOperationException("Stripe SecretKey is not configured (MK_STRIPE__SECRET_KEY).");

    StripeConfiguration.ApiKey = _opts.SecretKey;

    var svc = new SessionService();

    var options = new SessionCreateOptions
    {
      Mode = "payment",
      SuccessUrl = request.SuccessUrl,
      CancelUrl = request.CancelUrl,
      Metadata = new Dictionary<string, string>
      {
        ["order_id"] = request.OrderId.ToString(),
        ["order_payment_id"] = request.OrderPaymentId.ToString()
      },
      LineItems = request.LineItems.Select(li => new SessionLineItemOptions
      {
        Quantity = li.Quantity,
        PriceData = new SessionLineItemPriceDataOptions
        {
          Currency = request.CurrencyCode.ToLowerInvariant(),
          UnitAmount = li.UnitAmountCents,
          ProductData = new SessionLineItemPriceDataProductDataOptions
          {
            Name = li.Name
          }
        }
      }).ToList()
    };

    var session = await svc.CreateAsync(options, cancellationToken: ct);

    return new CreateOrderPaymentRedirectResult(
      ProviderCheckoutId: session.Id,
      RedirectUrl: session.Url);
  }
}
