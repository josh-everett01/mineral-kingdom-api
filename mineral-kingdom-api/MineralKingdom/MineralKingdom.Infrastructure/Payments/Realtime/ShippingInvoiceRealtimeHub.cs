using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Infrastructure.Payments.Realtime;

public sealed class ShippingInvoiceRealtimeHub
{
  private sealed class Subscriber
  {
    public required Channel<ShippingInvoiceRealtimeSnapshot> Channel { get; init; }
  }

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subs = new();

  public (Guid SubscriptionId, ChannelReader<ShippingInvoiceRealtimeSnapshot> Reader) Subscribe(Guid invoiceId)
  {
    var subscriptionId = Guid.NewGuid();

    var channel = Channel.CreateUnbounded<ShippingInvoiceRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false,
      AllowSynchronousContinuations = false
    });

    var group = _subs.GetOrAdd(invoiceId, _ => new ConcurrentDictionary<Guid, Subscriber>());
    group[subscriptionId] = new Subscriber { Channel = channel };

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid invoiceId, Guid subscriptionId)
  {
    if (_subs.TryGetValue(invoiceId, out var group) && group.TryRemove(subscriptionId, out var sub))
    {
      sub.Channel.Writer.TryComplete();
      if (group.IsEmpty)
        _subs.TryRemove(invoiceId, out _);
    }
  }

  public void Publish(Guid invoiceId, ShippingInvoiceRealtimeSnapshot snapshot)
  {
    if (!_subs.TryGetValue(invoiceId, out var group))
      return;

    foreach (var kv in group)
      kv.Value.Channel.Writer.TryWrite(snapshot);
  }
}