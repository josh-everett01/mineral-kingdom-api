namespace MineralKingdom.Infrastructure.Payments;

public interface IOrderRefundProvider
{
  string Provider { get; }

  Task<CreateRefundResult> RefundAsync(
    Guid orderId,
    long amountCents,
    string currencyCode,
    string reason,
    CancellationToken ct);
}

public sealed record CreateRefundResult(
  string ProviderRefundId);