using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize] // any authenticated user can create their draft order
public sealed class OrdersController : ControllerBase
{
  private readonly OrderSnapshotService _svc;

  public OrdersController(OrderSnapshotService svc) => _svc = svc;

  [HttpPost]
  public async Task<ActionResult<OrderIdResponse>> CreateDraft([FromBody] CreateOrderRequest req, CancellationToken ct)
  {
    var userId = User.GetUserId(); // consistent with your existing auth helpers
    var (ok, err, orderId) = await _svc.CreateDraftOrderAsync(userId, req, ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(new OrderIdResponse(orderId!.Value));
  }

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
}
