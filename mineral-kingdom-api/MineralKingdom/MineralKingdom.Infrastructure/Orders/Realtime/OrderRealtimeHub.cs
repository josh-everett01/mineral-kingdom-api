using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Infrastructure.Orders.Realtime;

public sealed class OrderRealtimeHub
{
  private sealed class Subscriber
  {
    public required Channel<OrderRealtimeSnapshot> Channel { get; init; }
  }

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subs = new();

  public (Guid SubscriptionId, ChannelReader<OrderRealtimeSnapshot> Reader) Subscribe(Guid orderId)
  {
    var subscriptionId = Guid.NewGuid();

    var channel = Channel.CreateUnbounded<OrderRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false,
      AllowSynchronousContinuations = false
    });

    var group = _subs.GetOrAdd(orderId, _ => new ConcurrentDictionary<Guid, Subscriber>());
    group[subscriptionId] = new Subscriber { Channel = channel };

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid orderId, Guid subscriptionId)
  {
    if (_subs.TryGetValue(orderId, out var group) && group.TryRemove(subscriptionId, out var sub))
    {
      sub.Channel.Writer.TryComplete();
      if (group.IsEmpty)
        _subs.TryRemove(orderId, out _);
    }
  }

  public void Publish(Guid orderId, OrderRealtimeSnapshot snapshot)
  {
    if (!_subs.TryGetValue(orderId, out var group))
      return;

    foreach (var kv in group)
      kv.Value.Channel.Writer.TryWrite(snapshot);
  }
}