namespace MineralKingdom.Infrastructure.Store.Realtime;

public interface ICartRealtimePublisher
{
  Task PublishCartAsync(Guid cartId, DateTimeOffset now, CancellationToken ct);
}