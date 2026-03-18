using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Payments.Realtime;

public sealed class CheckoutPaymentRealtimePublisher : ICheckoutPaymentRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly CheckoutPaymentRealtimeHub _hub;

  public CheckoutPaymentRealtimePublisher(
    MineralKingdomDbContext db,
    CheckoutPaymentRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishPaymentAsync(Guid paymentId, DateTimeOffset now, CancellationToken ct)
  {
    var payment = await _db.CheckoutPayments
      .AsNoTracking()
      .Where(x => x.Id == paymentId)
      .Select(x => new
      {
        x.Id,
        x.Status,
        x.HoldId
      })
      .SingleOrDefaultAsync(ct);

    if (payment is null)
      return;

    var orderId = await _db.Orders
      .AsNoTracking()
      .Where(x => x.CheckoutHoldId == payment.HoldId)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);

    var snapshot = new CheckoutPaymentRealtimeSnapshot(
      PaymentId: payment.Id,
      Status: payment.Status,
      OrderId: orderId,
      HoldId: payment.HoldId,
      EmittedAt: now);

    await _hub.PublishAsync(paymentId, snapshot, ct);
  }
}