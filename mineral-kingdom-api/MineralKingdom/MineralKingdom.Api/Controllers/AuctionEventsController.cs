using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Auctions.Realtime;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auctions/{auctionId:guid}/events")]
public sealed class AuctionEventsController : ControllerBase
{
  private readonly AuctionRealtimeHub _hub;
  private readonly IAuctionRealtimePublisher _publisher;

  public AuctionEventsController(AuctionRealtimeHub hub, IAuctionRealtimePublisher publisher)
  {
    _hub = hub;
    _publisher = publisher;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task Get([FromRoute] Guid auctionId, CancellationToken ct)
  {
    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers.ContentType = "text/event-stream";
    Response.Headers["X-Accel-Buffering"] = "no";

    var (subId, reader) = _hub.Subscribe(auctionId);

    try
    {
      // Initial snapshot (best-effort)
      try { await _publisher.PublishAuctionAsync(auctionId, DateTimeOffset.UtcNow, ct); }
      catch { /* don't fail SSE if snapshot publish fails */ }

      while (!ct.IsCancellationRequested)
      {
        var readTask = reader.ReadAsync(ct).AsTask();
        var keepAliveTask = Task.Delay(TimeSpan.FromSeconds(15), ct);

        Task completed;
        try
        {
          completed = await Task.WhenAny(readTask, keepAliveTask);
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (InvalidOperationException)
        {
          // TestHost sometimes throws when request is torn down
          break;
        }

        if (completed == keepAliveTask)
        {
          // keepalive comment
          try
          {
            await Response.WriteAsync($": ping {DateTimeOffset.UtcNow:O}\n\n", ct);
            await Response.Body.FlushAsync(ct);
          }
          catch (OperationCanceledException) { break; }
          catch (IOException) { break; }
          catch (InvalidOperationException) { break; }

          continue;
        }

        // completed == readTask
        AuctionRealtimeSnapshot snapshot;
        try
        {
          snapshot = await readTask;
        }
        catch (OperationCanceledException) { break; }
        catch (ChannelClosedException) { break; }
        catch (IOException) { break; }
        catch (InvalidOperationException) { break; }

        try
        {
          await WriteEventAsync("snapshot", snapshot, ct);
        }
        catch (OperationCanceledException) { break; }
        catch (IOException) { break; }
        catch (InvalidOperationException) { break; }
      }
    }
    finally
    {
      _hub.Unsubscribe(auctionId, subId);
    }
  }

  private async Task WriteEventAsync(string eventName, AuctionRealtimeSnapshot snap, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(snap);

    await Response.WriteAsync($"event: {eventName}\n", ct);
    await Response.WriteAsync($"data: {json}\n\n", ct);
    await Response.Body.FlushAsync(ct);
  }
}