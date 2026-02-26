using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Orders.Realtime;

public sealed class OrderRealtimePublisher : IOrderRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly OrderRealtimeHub _hub;

  public OrderRealtimePublisher(MineralKingdomDbContext db, OrderRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishOrderAsync(Guid orderId, DateTimeOffset now, CancellationToken ct)
  {
    var o = await _db.Orders
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == orderId, ct);

    if (o is null) return;

    _hub.Publish(orderId, new OrderRealtimeSnapshot(
      OrderId: o.Id,
      UserId: o.UserId,
      Status: o.Status,
      PaidAt: o.PaidAt,
      PaymentDueAt: o.PaymentDueAt,
      TotalCents: o.TotalCents,
      CurrencyCode: o.CurrencyCode,
      SourceType: o.SourceType,
      AuctionId: o.AuctionId,
      FulfillmentGroupId: o.FulfillmentGroupId,
      UpdatedAt: o.UpdatedAt
    ));
  }
}