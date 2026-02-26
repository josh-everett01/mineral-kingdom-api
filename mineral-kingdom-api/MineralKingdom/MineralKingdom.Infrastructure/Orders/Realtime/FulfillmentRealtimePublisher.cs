using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Orders.Realtime;

public sealed class FulfillmentRealtimePublisher : IFulfillmentRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly FulfillmentRealtimeHub _hub;

  public FulfillmentRealtimePublisher(MineralKingdomDbContext db, FulfillmentRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishFulfillmentAsync(Guid fulfillmentGroupId, DateTimeOffset now, CancellationToken ct)
  {
    var g = await _db.FulfillmentGroups
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == fulfillmentGroupId, ct);

    if (g is null) return;

    _hub.Publish(fulfillmentGroupId, new FulfillmentRealtimeSnapshot(
      FulfillmentGroupId: g.Id,
      UserId: g.UserId,
      Status: g.Status,
      BoxStatus: g.BoxStatus,
      PackedAt: g.PackedAt,
      ShippedAt: g.ShippedAt,
      DeliveredAt: g.DeliveredAt,
      ShippingCarrier: g.ShippingCarrier,
      TrackingNumber: g.TrackingNumber,
      UpdatedAt: g.UpdatedAt
    ));
  }
}