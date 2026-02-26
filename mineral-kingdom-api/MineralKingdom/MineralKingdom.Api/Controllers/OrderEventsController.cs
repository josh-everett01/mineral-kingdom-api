using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Orders.Realtime;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/events")]
public sealed class OrderEventsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly OrderRealtimeHub _hub;
  private readonly IOrderRealtimePublisher _publisher;

  public OrderEventsController(MineralKingdomDbContext db, OrderRealtimeHub hub, IOrderRealtimePublisher publisher)
  {
    _db = db;
    _hub = hub;
    _publisher = publisher;
  }

  [HttpGet]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified)]
  public async Task<IActionResult> Get([FromRoute] Guid orderId, CancellationToken ct)
  {
    var me = User.GetUserId();
    var isAdmin = User.IsInRole(UserRoles.Staff) || User.IsInRole(UserRoles.Owner);

    var ownerId = await _db.Orders
      .AsNoTracking()
      .Where(o => o.Id == orderId)
      .Select(o => o.UserId)
      .SingleOrDefaultAsync(ct);

    if (ownerId is null)
    {
      // Either doesn't exist OR guest order (no UserId). We don't stream guest orders in member dashboard.
      if (!await _db.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId, ct))
        return NotFound(new { error = "ORDER_NOT_FOUND" });

      if (!isAdmin) return Forbid();
    }

    if (!isAdmin && ownerId != me) return Forbid();

    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers.ContentType = "text/event-stream";
    Response.Headers["X-Accel-Buffering"] = "no";

    var (subId, reader) = _hub.Subscribe(orderId);

    try
    {
      // Initial snapshot (best-effort)
      try { await _publisher.PublishOrderAsync(orderId, DateTimeOffset.UtcNow, ct); }
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
        catch (OperationCanceledException) { break; }
        catch (InvalidOperationException) { break; }

        if (completed == keepAliveTask)
        {
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

        OrderRealtimeSnapshot snap;
        try
        {
          snap = await readTask;
        }
        catch (OperationCanceledException) { break; }
        catch (ChannelClosedException) { break; }
        catch (IOException) { break; }
        catch (InvalidOperationException) { break; }

        try
        {
          await WriteEventAsync("snapshot", snap, ct);
        }
        catch (OperationCanceledException) { break; }
        catch (IOException) { break; }
        catch (InvalidOperationException) { break; }
      }

      return new EmptyResult();
    }
    finally
    {
      _hub.Unsubscribe(orderId, subId);
    }
  }

  private async Task WriteEventAsync(string eventName, OrderRealtimeSnapshot snap, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(snap);
    await Response.WriteAsync($"event: {eventName}\n", ct);
    await Response.WriteAsync($"data: {json}\n\n", ct);
    await Response.Body.FlushAsync(ct);
  }
}