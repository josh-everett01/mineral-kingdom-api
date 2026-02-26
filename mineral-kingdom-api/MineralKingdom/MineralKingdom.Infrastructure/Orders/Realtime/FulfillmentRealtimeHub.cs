using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Infrastructure.Orders.Realtime;

public sealed class FulfillmentRealtimeHub
{
  private sealed class Subscriber
  {
    public required Channel<FulfillmentRealtimeSnapshot> Channel { get; init; }
  }

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subs = new();

  public (Guid SubscriptionId, ChannelReader<FulfillmentRealtimeSnapshot> Reader) Subscribe(Guid groupId)
  {
    var subscriptionId = Guid.NewGuid();

    var channel = Channel.CreateUnbounded<FulfillmentRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false,
      AllowSynchronousContinuations = false
    });

    var group = _subs.GetOrAdd(groupId, _ => new ConcurrentDictionary<Guid, Subscriber>());
    group[subscriptionId] = new Subscriber { Channel = channel };

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid groupId, Guid subscriptionId)
  {
    if (_subs.TryGetValue(groupId, out var group) && group.TryRemove(subscriptionId, out var sub))
    {
      sub.Channel.Writer.TryComplete();
      if (group.IsEmpty)
        _subs.TryRemove(groupId, out _);
    }
  }

  public void Publish(Guid groupId, FulfillmentRealtimeSnapshot snapshot)
  {
    if (!_subs.TryGetValue(groupId, out var group))
      return;

    foreach (var kv in group)
      kv.Value.Channel.Writer.TryWrite(snapshot);
  }
}