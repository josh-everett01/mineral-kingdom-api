using System.Text.Json;
using Microsoft.Extensions.Hosting;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using Npgsql;

namespace MineralKingdom.Api.Realtime;

public sealed class AuctionRealtimeNotificationListener : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly NpgsqlDataSource _dataSource;
  private readonly ILogger<AuctionRealtimeNotificationListener> _logger;

  public AuctionRealtimeNotificationListener(
    IServiceScopeFactory scopeFactory,
    NpgsqlDataSource dataSource,
    ILogger<AuctionRealtimeNotificationListener> logger)
  {
    _scopeFactory = scopeFactory;
    _dataSource = dataSource;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await ListenLoopAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Auction realtime notification listener failed. Retrying.");
        try
        {
          await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          break;
        }
      }
    }
  }

  private async Task ListenLoopAsync(CancellationToken ct)
  {
    var payloads = System.Threading.Channels.Channel.CreateUnbounded<string>(
      new System.Threading.Channels.UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
      });

    await using var conn = await _dataSource.OpenConnectionAsync(ct);

    conn.Notification += (_, e) =>
    {
      if (string.Equals(e.Channel, AuctionRealtimeNotification.ChannelName, StringComparison.Ordinal))
      {
        payloads.Writer.TryWrite(e.Payload);
      }
    };

    await using (var cmd = conn.CreateCommand())
    {
      cmd.CommandText = $"LISTEN {AuctionRealtimeNotification.ChannelName};";
      await cmd.ExecuteNonQueryAsync(ct);
    }

    _logger.LogInformation(
      "Listening for auction realtime notifications on channel {Channel}.",
      AuctionRealtimeNotification.ChannelName);

    while (!ct.IsCancellationRequested)
    {
      await conn.WaitAsync(ct);

      while (payloads.Reader.TryRead(out var payload))
      {
        await HandlePayloadAsync(payload, ct);
      }
    }
  }

  private async Task HandlePayloadAsync(string payload, CancellationToken ct)
  {
    AuctionRealtimeNotification? notification;

    try
    {
      notification = JsonSerializer.Deserialize<AuctionRealtimeNotification>(payload);
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Ignoring malformed auction realtime payload: {Payload}", payload);
      return;
    }

    if (notification is null || notification.AuctionId == Guid.Empty)
    {
      _logger.LogWarning("Ignoring empty auction realtime payload: {Payload}", payload);
      return;
    }

    using var scope = _scopeFactory.CreateScope();
    var publisher = scope.ServiceProvider.GetRequiredService<IAuctionRealtimePublisher>();

    await publisher.PublishAuctionAsync(notification.AuctionId, DateTimeOffset.UtcNow, ct);
  }
}