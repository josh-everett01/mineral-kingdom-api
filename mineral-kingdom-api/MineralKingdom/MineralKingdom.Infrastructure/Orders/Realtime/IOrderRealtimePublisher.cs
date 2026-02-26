namespace MineralKingdom.Infrastructure.Orders.Realtime;

public interface IOrderRealtimePublisher
{
  Task PublishOrderAsync(Guid orderId, DateTimeOffset now, CancellationToken ct);
}