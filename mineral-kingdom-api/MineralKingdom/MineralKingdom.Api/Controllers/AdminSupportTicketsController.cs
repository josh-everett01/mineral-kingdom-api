using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Support;
using MineralKingdom.Infrastructure.Support;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/support/tickets")]
[Authorize(Roles = $"{UserRoles.Owner},{UserRoles.Staff}")]
public sealed class AdminSupportTicketsController : ControllerBase
{
  private readonly SupportTicketService _support;

  public AdminSupportTicketsController(SupportTicketService support)
  {
    _support = support;
  }

  [HttpGet]
  public async Task<IActionResult> List(
    [FromQuery] string? status,
    [FromQuery] string? priority,
    [FromQuery] Guid? assignedToUserId,
    [FromQuery] string? q,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken ct = default)
  {
    var rows = await _support.AdminListAsync(status, priority, assignedToUserId, q, page, pageSize, ct);
    return Ok(rows);
  }

  [HttpGet("{ticketId:guid}")]
  public async Task<IActionResult> Get([FromRoute] Guid ticketId, CancellationToken ct)
  {
    var me = User.GetUserId();
    var (ok, err, dto) = await _support.GetTicketForMemberAsync(ticketId, me, isAdmin: true, ct);
    if (!ok) return err == "NOT_FOUND" ? NotFound(new { error = err }) : BadRequest(new { error = err });
    return Ok(dto);
  }

  [HttpPatch("{ticketId:guid}")]
  public async Task<IActionResult> Update([FromRoute] Guid ticketId, [FromBody] AdminUpdateSupportTicketRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var (ok, err) = await _support.AdminUpdateAsync(ticketId, req, now, ct);
    if (!ok) return err == "NOT_FOUND" ? NotFound(new { error = err }) : BadRequest(new { error = err });

    return NoContent();
  }

  [HttpPost("{ticketId:guid}/messages")]
  public async Task<IActionResult> Reply([FromRoute] Guid ticketId, [FromBody] AdminCreateSupportMessageRequest req, CancellationToken ct)
  {
    var adminUserId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;
    var apiBase = $"{Request.Scheme}://{Request.Host}";

    var (ok, err) = await _support.AdminAddMessageAsync(ticketId, adminUserId, req, apiBase, now, ct);
    if (!ok) return err == "NOT_FOUND" ? NotFound(new { error = err }) : BadRequest(new { error = err });

    return NoContent();
  }
}