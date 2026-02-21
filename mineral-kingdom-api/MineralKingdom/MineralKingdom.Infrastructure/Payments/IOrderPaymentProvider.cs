using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Infrastructure.Payments;

public interface IOrderPaymentProvider
{
  string Provider { get; }
  Task<CreateOrderPaymentRedirectResult> CreateRedirectAsync(CreateOrderPaymentRedirectRequest request, CancellationToken ct);
}
