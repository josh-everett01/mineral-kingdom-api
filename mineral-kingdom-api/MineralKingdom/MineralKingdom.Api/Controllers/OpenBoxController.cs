using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/me/open-box")]
[Authorize(Policy = AuthorizationPolicies.EmailVerified)]
public sealed class OpenBoxController : ControllerBase
{
  private readonly OpenBoxService _openBox;
  private readonly MineralKingdomDbContext _db;

  public OpenBoxController(OpenBoxService openBox, MineralKingdomDbContext db)
  {
    _openBox = openBox;
    _db = db;
  }

  // Read current open-box / shipment-request group for user.
  // Prefer an active OPEN box first; otherwise return the most recent
  // LOCKED_FOR_REVIEW/CLOSED/SHIPPED group so the customer can see next steps.
  [HttpGet]
  public async Task<ActionResult<OpenBoxDto>> GetCurrent(CancellationToken ct)
  {
    var userId = User.GetUserId();

    var group = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.UserId == userId)
      .OrderBy(g =>
        g.BoxStatus == "OPEN" ? 0 :
        g.BoxStatus == "LOCKED_FOR_REVIEW" ? 1 :
        g.BoxStatus == "CLOSED" ? 2 :
        g.BoxStatus == "SHIPPED" ? 3 : 4)
      .ThenByDescending(g => g.UpdatedAt)
      .FirstOrDefaultAsync(ct);

    if (group is null)
      return NotFound(new { error = "OPEN_BOX_NOT_FOUND" });

    return Ok(await BuildDtoAsync(group.Id, ct));
  }

  // Create or return current open box (idempotent)
  [HttpPost]
  public async Task<ActionResult<OpenBoxDto>> GetOrCreate(CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err, box) = await _openBox.GetOrCreateOpenBoxAsync(userId, now, ct);
    if (!ok || box is null) return BadRequest(new { error = err });

    return Ok(await BuildDtoAsync(box.Id, ct));
  }

  [HttpPost("orders/{orderId:guid}")]
  public async Task<IActionResult> AddOrder(Guid orderId, CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err) = await _openBox.AddOrderToOpenBoxAsync(userId, orderId, now, ct);
    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpDelete("orders/{orderId:guid}")]
  public async Task<IActionResult> RemoveOrder(Guid orderId, CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err) = await _openBox.RemoveOrderFromOpenBoxAsync(userId, orderId, now, ct);
    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("close")]
  public async Task<IActionResult> Close(CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var (ok, err) = await _openBox.CloseOpenBoxAsync(userId, now, ct);
    if (!ok) return BadRequest(new { error = err });

    return NoContent();
  }

  private async Task<OpenBoxDto> BuildDtoAsync(Guid groupId, CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups.AsNoTracking()
      .SingleAsync(g => g.Id == groupId, ct);

    var orders = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId == groupId)
      .OrderBy(o => o.CreatedAt)
      .Select(o => new OpenBoxOrderDto(
        o.Id,
        o.OrderNumber,
        o.TotalCents,
        o.CurrencyCode,
        o.Status))
      .ToListAsync(ct);

    return new OpenBoxDto(
      FulfillmentGroupId: box.Id,
      BoxStatus: box.BoxStatus,
      ShipmentRequestStatus: box.ShipmentRequestStatus,
      FulfillmentStatus: box.Status,
      ClosedAt: box.ClosedAt,
      OrderCount: orders.Count,
      Orders: orders);
  }
}