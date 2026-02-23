using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/fulfillment")]
[Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
public sealed class AdminFulfillmentController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  public AdminFulfillmentController(MineralKingdomDbContext db) => _db = db;

  // DoD: Admin can see open boxes and contained orders
  [HttpGet("open-boxes")]
  public async Task<IActionResult> ListOpenBoxes(CancellationToken ct)
  {
    var boxes = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.BoxStatus == "OPEN")
      .OrderByDescending(g => g.UpdatedAt)
      .Select(g => new
      {
        fulfillmentGroupId = g.Id,
        userId = g.UserId,
        boxStatus = g.BoxStatus,
        fulfillmentStatus = g.Status,
        createdAt = g.CreatedAt,
        updatedAt = g.UpdatedAt
      })
      .ToListAsync(ct);

    var ids = boxes.Select(b => (Guid)b.fulfillmentGroupId).ToList();

    var counts = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId != null && ids.Contains(o.FulfillmentGroupId.Value))
      .GroupBy(o => o.FulfillmentGroupId!.Value)
      .Select(g => new { groupId = g.Key, count = g.Count() })
      .ToListAsync(ct);

    var countById = counts.ToDictionary(x => x.groupId, x => x.count);

    var result = boxes.Select(b => new
    {
      b.fulfillmentGroupId,
      b.userId,
      b.boxStatus,
      b.fulfillmentStatus,
      b.createdAt,
      b.updatedAt,
      orderCount = countById.TryGetValue((Guid)b.fulfillmentGroupId, out var c) ? c : 0
    });

    return Ok(result);
  }

  [HttpGet("groups/{groupId:guid}")]
  public async Task<ActionResult<OpenBoxDto>> GetGroup(Guid groupId, CancellationToken ct)
  {
    var box = await _db.FulfillmentGroups.AsNoTracking()
      .SingleOrDefaultAsync(g => g.Id == groupId, ct);

    if (box is null) return NotFound(new { error = "GROUP_NOT_FOUND" });

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

    return Ok(new OpenBoxDto(
      FulfillmentGroupId: box.Id,
      BoxStatus: box.BoxStatus,
      FulfillmentStatus: box.Status,
      ClosedAt: box.ClosedAt,
      OrderCount: orders.Count,
      Orders: orders));
  }
}