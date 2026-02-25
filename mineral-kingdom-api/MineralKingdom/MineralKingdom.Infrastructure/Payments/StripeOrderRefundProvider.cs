using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class StripeOrderRefundProvider : IOrderRefundProvider
{
  private readonly MineralKingdomDbContext _db;
  private readonly PaymentsOptions _payments;

  public StripeOrderRefundProvider(MineralKingdomDbContext db, IOptions<PaymentsOptions> payments)
  {
    _db = db;
    _payments = payments.Value;
  }

  public string Provider => PaymentProviders.Stripe;

  public async Task<CreateRefundResult> RefundAsync(
    Guid orderId,
    long amountCents,
    string currencyCode,
    string reason,
    CancellationToken ct)
  {
    // FAKE mode: deterministic ID (tests/dev)
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
      return new CreateRefundResult($"re_fake_{orderId:N}_{amountCents}");

    // TODO: real Stripe refund call later.
    // We intentionally block real-mode until Stripe refund API is wired, to avoid silent no-op.
    throw new InvalidOperationException("STRIPE_REFUNDS_NOT_IMPLEMENTED");
  }
}