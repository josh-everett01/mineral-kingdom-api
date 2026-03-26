using System.Text.Json;
using Npgsql;

namespace MineralKingdom.Infrastructure.Auctions.Realtime;

public sealed class PostgresAuctionRealtimeNotifier : IAuctionRealtimeNotifier
{
  private readonly NpgsqlDataSource _dataSource;

  public PostgresAuctionRealtimeNotifier(NpgsqlDataSource dataSource)
  {
    _dataSource = dataSource;
  }

  public async Task NotifyAuctionChangedAsync(Guid auctionId, CancellationToken ct)
  {
    var payload = JsonSerializer.Serialize(new AuctionRealtimeNotification(auctionId));

    await using var conn = await _dataSource.OpenConnectionAsync(ct);
    await using var cmd = conn.CreateCommand();

    cmd.CommandText = "select pg_notify(@channel, @payload)";
    cmd.Parameters.AddWithValue("channel", AuctionRealtimeNotification.ChannelName);
    cmd.Parameters.AddWithValue("payload", payload);

    await cmd.ExecuteNonQueryAsync(ct);
  }
}