using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Payments.Realtime;

public sealed class ShippingInvoiceRealtimePublisher : IShippingInvoiceRealtimePublisher
{
  private readonly MineralKingdomDbContext _db;
  private readonly ShippingInvoiceRealtimeHub _hub;

  public ShippingInvoiceRealtimePublisher(MineralKingdomDbContext db, ShippingInvoiceRealtimeHub hub)
  {
    _db = db;
    _hub = hub;
  }

  public async Task PublishInvoiceAsync(Guid shippingInvoiceId, DateTimeOffset now, CancellationToken ct)
  {
    var inv = await _db.ShippingInvoices
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == shippingInvoiceId, ct);

    if (inv is null) return;

    _hub.Publish(shippingInvoiceId, new ShippingInvoiceRealtimeSnapshot(
      ShippingInvoiceId: inv.Id,
      FulfillmentGroupId: inv.FulfillmentGroupId,
      Status: inv.Status,
      PaidAt: inv.PaidAt,
      AmountCents: inv.AmountCents,
      CurrencyCode: inv.CurrencyCode,
      Provider: inv.Provider,
      ProviderCheckoutId: inv.ProviderCheckoutId,
      UpdatedAt: inv.UpdatedAt
    ));
  }
}