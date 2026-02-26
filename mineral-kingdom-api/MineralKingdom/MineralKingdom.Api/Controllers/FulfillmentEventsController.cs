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
[Route("api/fulfillment-groups/{groupId:guid}/events")]
public sealed class FulfillmentEventsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly FulfillmentRealtimeHub _hub;
  private readonly IFulfillmentRealtimePublisher _publisher;

  public FulfillmentEventsController(MineralKingdomDbContext db, FulfillmentRealtimeHub hub, IFulfillmentRealtimePublisher publisher)
  {
    _db = db;
    _hub = hub;
    _publisher = publisher;
  }

  [HttpGet]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified)]
  public async Task<IActionResult> Get([FromRoute] Guid groupId, CancellationToken ct)
  {
    var me = User.GetUserId();
    var isAdmin = User.IsInRole(UserRoles.Staff) || User.IsInRole(UserRoles.Owner);

    var ownerId = await _db.FulfillmentGroups
      .AsNoTracking()
      .Where(g => g.Id == groupId)
      .Select(g => g.UserId)
      .SingleOrDefaultAsync(ct);

    var exists = await _db.FulfillmentGroups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct);
    if (!exists) return NotFound(new { error = "GROUP_NOT_FOUND" });

    if (!isAdmin && ownerId != me) return Forbid();

    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers.ContentType = "text/event-stream";
    Response.Headers["X-Accel-Buffering"] = "no";

    var (subId, reader) = _hub.Subscribe(groupId);

    try
    {
      // Initial snapshot (best-effort)
      try { await _publisher.PublishFulfillmentAsync(groupId, DateTimeOffset.UtcNow, ct); }
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

        FulfillmentRealtimeSnapshot snap;
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
      _hub.Unsubscribe(groupId, subId);
    }
  }

  private async Task WriteEventAsync(string eventName, FulfillmentRealtimeSnapshot snap, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(snap);
    await Response.WriteAsync($"event: {eventName}\n", ct);
    await Response.WriteAsync($"data: {json}\n\n", ct);
    await Response.Body.FlushAsync(ct);
  }
}