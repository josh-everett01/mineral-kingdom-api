using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Infrastructure.Payments.Realtime;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/checkout-payments/{paymentId:guid}/events")]
public sealed class CheckoutPaymentEventsController : ControllerBase
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly CheckoutPaymentRealtimeHub _hub;
  private readonly ICheckoutPaymentRealtimePublisher _publisher;

  public CheckoutPaymentEventsController(
    CheckoutPaymentRealtimeHub hub,
    ICheckoutPaymentRealtimePublisher publisher)
  {
    _hub = hub;
    _publisher = publisher;
  }

  [HttpGet]
  public async Task Get(Guid paymentId, CancellationToken ct)
  {
    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers.ContentType = "text/event-stream";
    Response.Headers["X-Accel-Buffering"] = "no";

    var (subscriptionId, reader) = _hub.Subscribe(paymentId);

    try
    {
      await _publisher.PublishPaymentAsync(paymentId, DateTimeOffset.UtcNow, ct);

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
      _hub.Unsubscribe(paymentId, subscriptionId);
    }
  }
}