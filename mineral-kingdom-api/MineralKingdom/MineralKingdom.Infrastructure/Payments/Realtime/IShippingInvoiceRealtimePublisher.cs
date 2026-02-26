namespace MineralKingdom.Infrastructure.Payments.Realtime;

public interface IShippingInvoiceRealtimePublisher
{
  Task PublishInvoiceAsync(Guid shippingInvoiceId, DateTimeOffset now, CancellationToken ct);
}