using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
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
  private readonly AuctionShippingChoiceService _auctionShippingChoices;

  public OrdersController(
    OrderSnapshotService svc,
    OrderService orders,
    AuctionShippingChoiceService auctionShippingChoices)
  {
    _svc = svc;
    _orders = orders;
    _auctionShippingChoices = auctionShippingChoices;
  }

  [Authorize]
  [HttpPost]
  public async Task<ActionResult<OrderIdResponse>> CreateDraft([FromBody] CreateOrderRequest req, CancellationToken ct)
  {
    var userId = User.GetUserId();
    var (ok, err, orderId) = await _svc.CreateDraftOrderAsync(userId, req, ct);

    if (!ok) return BadRequest(new { error = err });

    return Ok(new OrderIdResponse(orderId!.Value));
  }

  [Authorize]
  [HttpGet]
  public async Task<ActionResult<List<OrderDto>>> ListMine(CancellationToken ct)
  {
    var userId = User.GetUserId();
    var orders = await _svc.ListForUserAsync(userId, ct);
    return Ok(orders);
  }

  [Authorize]
  [HttpGet("{id:guid}")]
  public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
  {
    var dto = await _svc.GetOrderAsync(id, ct);
    if (dto is null) return NotFound(new { error = "ORDER_NOT_FOUND" });

    var userId = User.GetUserId();
    if (dto.UserId != userId) return Forbid();

    return Ok(dto);
  }

  [Authorize]
  [HttpPost("{id:guid}/auction-shipping-choice")]
  public async Task<ActionResult<AuctionShippingChoiceResponse>> SetAuctionShippingChoice(
    Guid id,
    [FromBody] SetAuctionShippingChoiceRequest req,
    CancellationToken ct)
  {
    var userId = User.GetUserId();

    var (ok, err, response) = await _auctionShippingChoices.SetChoiceAsync(
      id,
      userId,
      req.ShippingMode,
      ct);

    if (!ok)
      return BadRequest(new { error = err });

    return Ok(response);
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