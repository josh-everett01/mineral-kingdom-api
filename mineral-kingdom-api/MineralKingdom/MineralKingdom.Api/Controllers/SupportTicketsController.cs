using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Support;
using MineralKingdom.Infrastructure.Support;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/support/tickets")]
public sealed class SupportTicketsController : ControllerBase
{
  private readonly SupportTicketService _support;
  private readonly IAuthorizationService _authz;

  public SupportTicketsController(SupportTicketService support, IAuthorizationService authz)
  {
    _support = support;
    _authz = authz;
  }

  [HttpPost]
  [EnableRateLimiting("support")]
  public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var apiBase = $"{Request.Scheme}://{Request.Host}";

    Guid? memberUserId = null;

    if (User.Identity?.IsAuthenticated == true)
    {
      var ok = await _authz.AuthorizeAsync(User, policyName: AuthorizationPolicies.EmailVerified);
      if (!ok.Succeeded) return Forbid();

      memberUserId = User.GetUserId();
    }

    var (created, err, resp) = await _support.CreateTicketAsync(memberUserId, req, apiBase, now, ct);
    if (!created) return BadRequest(new { error = err });

    return Created($"/api/support/tickets/{resp!.TicketId}", resp);
  }

  [HttpGet("{ticketId:guid}")]
  public async Task<IActionResult> Get([FromRoute] Guid ticketId, [FromQuery] string? token, CancellationToken ct)
  {
    var isAdmin = User.IsInRole(UserRoles.Owner) || User.IsInRole(UserRoles.Staff);

    if (User.Identity?.IsAuthenticated == true)
    {
      var ok = await _authz.AuthorizeAsync(User, policyName: AuthorizationPolicies.EmailVerified);
      if (!ok.Succeeded) return Forbid();

      var me = User.GetUserId();
      var (got, err, dto) = await _support.GetTicketForMemberAsync(ticketId, me, isAdmin, ct);
      if (!got) return err == "NOT_FOUND" ? NotFound(new { error = err }) : Forbid();
      return Ok(dto);
    }

    var (gok, gerr, gdto) = await _support.GetTicketForGuestAsync(ticketId, token ?? "", ct);
    if (!gok) return gerr == "NOT_FOUND" ? NotFound(new { error = gerr }) : Unauthorized();
    return Ok(gdto);
  }

  [HttpPost("{ticketId:guid}/messages")]
  public async Task<IActionResult> Reply([FromRoute] Guid ticketId, [FromQuery] string? token, [FromBody] CreateSupportMessageRequest req, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    if (req is null) return BadRequest(new { error = "INVALID_REQUEST" });

    if (User.Identity?.IsAuthenticated == true)
    {
      var ok = await _authz.AuthorizeAsync(User, policyName: AuthorizationPolicies.EmailVerified);
      if (!ok.Succeeded) return Forbid();

      var me = User.GetUserId();
      var (rok, err) = await _support.AddCustomerMessageAsMemberAsync(ticketId, me, req.Message, now, ct);
      if (!rok) return err == "NOT_FOUND" ? NotFound(new { error = err }) : Forbid();
      return NoContent();
    }

    var (gok, gerr) = await _support.AddCustomerMessageAsGuestAsync(ticketId, token ?? "", req.Message, now, ct);
    if (!gok) return gerr == "NOT_FOUND" ? NotFound(new { error = gerr }) : Unauthorized();
    return NoContent();
  }
}