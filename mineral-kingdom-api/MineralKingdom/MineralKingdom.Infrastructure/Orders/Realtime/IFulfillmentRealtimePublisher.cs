namespace MineralKingdom.Infrastructure.Orders.Realtime;

public interface IFulfillmentRealtimePublisher
{
  Task PublishFulfillmentAsync(Guid fulfillmentGroupId, DateTimeOffset now, CancellationToken ct);
}