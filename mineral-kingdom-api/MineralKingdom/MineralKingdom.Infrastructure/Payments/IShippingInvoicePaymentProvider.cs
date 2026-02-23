using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Infrastructure.Payments;

public interface IShippingInvoicePaymentProvider
{
  string Provider { get; }

  Task<CreateShippingInvoicePaymentRedirectResult> CreateRedirectAsync(
    Guid shippingInvoiceId,
    Guid fulfillmentGroupId,
    long amountCents,
    string currencyCode,
    string successUrl,
    string cancelUrl,
    CancellationToken ct);
}