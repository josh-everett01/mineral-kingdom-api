using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Infrastructure.Store.Realtime;

public sealed class CartRealtimeHub
{
  private sealed record Subscriber(Channel<CartRealtimeSnapshot> Channel);

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subscriptions = new();

  public (Guid SubscriptionId, ChannelReader<CartRealtimeSnapshot> Reader) Subscribe(Guid cartId)
  {
    var subscriptionId = Guid.NewGuid();
    var channel = Channel.CreateUnbounded<CartRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false
    });

    var subscribers = _subscriptions.GetOrAdd(cartId, _ => new ConcurrentDictionary<Guid, Subscriber>());
    subscribers[subscriptionId] = new Subscriber(channel);

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid cartId, Guid subscriptionId)
  {
    if (!_subscriptions.TryGetValue(cartId, out var subscribers))
      return;

    if (subscribers.TryRemove(subscriptionId, out var subscriber))
    {
      subscriber.Channel.Writer.TryComplete();
    }

    if (subscribers.IsEmpty)
    {
      _subscriptions.TryRemove(cartId, out _);
    }
  }

  public ValueTask PublishAsync(Guid cartId, CartRealtimeSnapshot snapshot, CancellationToken ct)
  {
    if (!_subscriptions.TryGetValue(cartId, out var subscribers))
      return ValueTask.CompletedTask;

    foreach (var subscriber in subscribers.Values)
    {
      subscriber.Channel.Writer.TryWrite(snapshot);
    }

    return ValueTask.CompletedTask;
  }
}