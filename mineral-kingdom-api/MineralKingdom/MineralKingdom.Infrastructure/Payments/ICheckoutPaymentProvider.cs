using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Infrastructure.Payments;

public interface ICheckoutPaymentProvider
{
  string Provider { get; }

  Task<CreatePaymentRedirectResult> CreateRedirectAsync(CreatePaymentRedirectRequest request, CancellationToken ct);
}

public sealed record CreatePaymentRedirectRequest(
  Guid HoldId,
  Guid PaymentId,
  string CurrencyCode,
  int AmountCents,
  IReadOnlyList<CheckoutLineItem> LineItems,
  string SuccessUrl,
  string CancelUrl
);

public sealed record CheckoutLineItem(
  string Name,
  long UnitAmountCents,
  long Quantity
);

public sealed record CreatePaymentRedirectResult(
  string ProviderCheckoutId,
  string RedirectUrl
);
