using System.Collections.Concurrent;
using System.Threading.Channels;
using MineralKingdom.Contracts.Auctions;

namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public sealed class AuctionRealtimeHub
{
  private sealed class Subscriber
  {
    public required Channel<AuctionRealtimeSnapshot> Channel { get; init; }
  }

  private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscriber>> _subs = new();

  public (Guid SubscriptionId, ChannelReader<AuctionRealtimeSnapshot> Reader) Subscribe(Guid auctionId)
  {
    var subscriptionId = Guid.NewGuid();

    var channel = Channel.CreateUnbounded<AuctionRealtimeSnapshot>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false,
      AllowSynchronousContinuations = false
    });

    var group = _subs.GetOrAdd(auctionId, _ => new ConcurrentDictionary<Guid, Subscriber>());
    group[subscriptionId] = new Subscriber { Channel = channel };

    return (subscriptionId, channel.Reader);
  }

  public void Unsubscribe(Guid auctionId, Guid subscriptionId)
  {
    if (_subs.TryGetValue(auctionId, out var group) && group.TryRemove(subscriptionId, out var sub))
    {
      sub.Channel.Writer.TryComplete();
      if (group.IsEmpty)
        _subs.TryRemove(auctionId, out _);
    }
  }

  public void Publish(Guid auctionId, AuctionRealtimeSnapshot snapshot)
  {
    if (!_subs.TryGetValue(auctionId, out var group))
      return;

    foreach (var kv in group)
    {
      // Best-effort; if a client is slow or disconnected, we don’t want to block.
      kv.Value.Channel.Writer.TryWrite(snapshot);
    }
  }
}