using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Infrastructure.Store;
using MineralKingdom.Infrastructure.Store.Realtime;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/carts/{cartId:guid}/events")]
public sealed class CartEventsController : ControllerBase
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly CartRealtimeHub _hub;
  private readonly ICartRealtimePublisher _publisher;
  private readonly CartService _cartService;

  public CartEventsController(
    CartRealtimeHub hub,
    ICartRealtimePublisher publisher,
    CartService cartService)
  {
    _hub = hub;
    _publisher = publisher;
    _cartService = cartService;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task Get(Guid cartId, CancellationToken ct)
  {
    var userId = TryGetUserId();
    var cart = await _cartService.GetCartForResponseAsync(cartId, userId, ct);
    if (cart is null)
    {
      Response.StatusCode = StatusCodes.Status404NotFound;
      return;
    }

    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers.ContentType = "text/event-stream";
    Response.Headers["X-Accel-Buffering"] = "no";

    var (subscriptionId, reader) = _hub.Subscribe(cartId);

    try
    {
      await _publisher.PublishCartAsync(cartId, DateTimeOffset.UtcNow, ct);

      while (!ct.IsCancellationRequested)
      {
        var readTask = reader.ReadAsync(ct).AsTask();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(15), ct);

        var completed = await Task.WhenAny(readTask, delayTask);

        if (completed == delayTask)
        {
          await Response.WriteAsync($": ping {DateTimeOffset.UtcNow:O}\n\n", ct);
          await Response.Body.FlushAsync(ct);
          continue;
        }

        var snapshot = await readTask;
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        await Response.WriteAsync("event: snapshot\n", ct);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
      }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
      _hub.Unsubscribe(cartId, subscriptionId);
    }
  }

  private Guid? TryGetUserId()
  {
    var raw = User.FindFirst("sub")?.Value
              ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    return Guid.TryParse(raw, out var id) ? id : null;
  }
}