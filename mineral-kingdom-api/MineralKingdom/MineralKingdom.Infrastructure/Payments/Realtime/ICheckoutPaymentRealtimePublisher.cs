namespace MineralKingdom.Infrastructure.Payments.Realtime;

public interface ICheckoutPaymentRealtimePublisher
{
  Task PublishPaymentAsync(Guid paymentId, DateTimeOffset now, CancellationToken ct);
}