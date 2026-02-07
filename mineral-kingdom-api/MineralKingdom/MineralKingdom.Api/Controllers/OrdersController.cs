using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
  private readonly OrderSnapshotService _svc;

  private readonly OrderService _orders;

  public OrdersController(OrderSnapshotService svc, OrderService orders)
  {
    _svc = svc;
    _orders = orders;
  }

  [Authorize]
  [HttpPost]
  public async Task<ActionResult<OrderIdResponse>> CreateDraft([FromBody] CreateOrderRequest req, CancellationToken ct)
  {
    var userId = User.GetUserId(); // consistent with your existing auth helpers
    var (ok, err, orderId) = await _svc.CreateDraftOrderAsync(userId, req, ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(new OrderIdResponse(orderId!.Value));
  }

  [Authorize]
  [HttpGet("{id:guid}")]
  public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
  {
    var dto = await _svc.GetOrderAsync(id, ct);
    if (dto is null) return NotFound(new { error = "ORDER_NOT_FOUND" });

    // optional: enforce ownership
    var userId = User.GetUserId();
    if (dto.UserId != userId) return Forbid();

    return Ok(dto);
  }

  [AllowAnonymous]
  [HttpGet("lookup")]
  public async Task<IActionResult> Lookup([FromQuery] string orderNumber, [FromQuery] string email, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(email))
      return BadRequest(new { error = "INVALID_REQUEST" });

    var normalizedEmail = email.Trim().ToLowerInvariant();
    var normalizedOrderNumber = orderNumber.Trim();

    var dto = await _orders.GetGuestOrderAsync(normalizedOrderNumber, normalizedEmail, ct);

    if (dto is null) return NotFound(new { error = "ORDER_NOT_FOUND" });

    return Ok(dto);
  }
}
