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
    var order = await _db.Orders
      .AsNoTracking()
      .Where(x => x.Id == orderId)
      .Select(x => new
      {
        x.Id,
        x.UserId,
        x.OrderNumber,
        x.Status,
        x.PaidAt,
        x.PaymentDueAt,
        x.TotalCents,
        x.CurrencyCode,
        x.SourceType,
        x.AuctionId,
        x.FulfillmentGroupId,
        x.UpdatedAt
      })
      .SingleOrDefaultAsync(ct);

    if (order is null) return;

    var latestPayment = await _db.OrderPayments
      .AsNoTracking()
      .Where(p => p.OrderId == orderId)
      .OrderByDescending(p => p.CreatedAt)
      .ThenByDescending(p => p.Id)
      .Select(p => new
      {
        p.Status,
        p.Provider
      })
      .FirstOrDefaultAsync(ct);

    _hub.Publish(orderId, new OrderRealtimeSnapshot(
      OrderId: order.Id,
      UserId: order.UserId,
      OrderNumber: order.OrderNumber,
      Status: order.Status,
      PaymentStatus: latestPayment?.Status,
      PaymentProvider: latestPayment?.Provider,
      PaidAt: order.PaidAt,
      PaymentDueAt: order.PaymentDueAt,
      TotalCents: order.TotalCents,
      CurrencyCode: order.CurrencyCode,
      SourceType: order.SourceType,
      AuctionId: order.AuctionId,
      FulfillmentGroupId: order.FulfillmentGroupId,
      UpdatedAt: order.UpdatedAt,
      NewTimelineEntries: null
    ));
  }
}