using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class PayPalOrderRefundProvider : IOrderRefundProvider
{
  private readonly PaymentsOptions _payments;

  public PayPalOrderRefundProvider(IOptions<PaymentsOptions> payments)
  {
    _payments = payments.Value;
  }

  public string Provider => PaymentProviders.PayPal;

  public Task<CreateRefundResult> RefundAsync(
    Guid orderId,
    long amountCents,
    string currencyCode,
    string reason,
    CancellationToken ct)
  {
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
      return Task.FromResult(new CreateRefundResult($"pp_fake_refund_{orderId:N}_{amountCents}"));

    throw new InvalidOperationException("PAYPAL_REFUNDS_NOT_IMPLEMENTED");
  }
}