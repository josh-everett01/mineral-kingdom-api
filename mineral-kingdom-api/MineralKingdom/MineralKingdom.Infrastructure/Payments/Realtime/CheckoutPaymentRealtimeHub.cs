using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Store;

namespace MineralKingdom.Infrastructure.Payments.Realtime;

public sealed class CheckoutPaymentRealtimeHub
{
  private sealed record Subscriber(Channel<CheckoutPaymentRealtimeSnapshot> Channel);

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subscriptions = new();

  public (Guid SubscriptionId, ChannelReader<CheckoutPaymentRealtimeSnapshot> Reader) Subscribe(Guid paymentId)
  {
    var subscriptionId = Guid.NewGuid();

    var channel = Channel.CreateUnbounded<CheckoutPaymentRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false
    });

    var subscribers = _subscriptions.GetOrAdd(
      paymentId,
      static _ => new ConcurrentDictionary<Guid, Subscriber>());

    subscribers[subscriptionId] = new Subscriber(channel);

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid paymentId, Guid subscriptionId)
  {
    if (!_subscriptions.TryGetValue(paymentId, out var subscribers))
      return;

    if (subscribers.TryRemove(subscriptionId, out var subscriber))
      subscriber.Channel.Writer.TryComplete();

    if (subscribers.IsEmpty)
      _subscriptions.TryRemove(paymentId, out _);
  }

  public ValueTask PublishAsync(
    Guid paymentId,
    CheckoutPaymentRealtimeSnapshot snapshot,
    CancellationToken ct)
  {
    if (!_subscriptions.TryGetValue(paymentId, out var subscribers))
      return ValueTask.CompletedTask;

    foreach (var subscriber in subscribers.Values)
      subscriber.Channel.Writer.TryWrite(snapshot);

    return ValueTask.CompletedTask;
  }
}