using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminUsersController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminUsersController(MineralKingdomDbContext db) => _db = db;

  public sealed record AdminUserResponse(Guid Id, string Email, bool EmailVerified, string Role);

  [HttpGet("{userId:guid}")]
  public async Task<ActionResult<AdminUserResponse>> GetUser(Guid userId, CancellationToken ct)
  {
    var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (user is null) return NotFound(new { error = "USER_NOT_FOUND" });

    return Ok(new AdminUserResponse(user.Id, user.Email, user.EmailVerified, user.Role));
  }

  public sealed record SetRoleRequest(string Role);

  [HttpPut("{userId:guid}/role")]
  [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
  public async Task<IActionResult> SetRole(Guid userId, [FromBody] SetRoleRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Role) || !UserRoles.IsValid(req.Role))
      return BadRequest(new { error = "INVALID_ROLE" });

    var actorIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    if (!Guid.TryParse(actorIdRaw, out var actorId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    // Ensure actor exists (tightens security vs spoofed TestAuth headers)
    var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId, ct);
    if (!actorExists)
      return Unauthorized(new { error = "ACTOR_NOT_FOUND" });

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var target = await _db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (target is null) return NotFound(new { error = "USER_NOT_FOUND" });

    var normalizedNewRole = req.Role.Trim().ToUpperInvariant();
    var before = target.Role;

    // Prevent OWNER lockout: cannot remove own OWNER
    if (actorId == userId &&
        string.Equals(target.Role, UserRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedNewRole, UserRoles.Owner, StringComparison.OrdinalIgnoreCase))
    {
      return Conflict(new { error = "CANNOT_DEMOTE_SELF_OWNER" });
    }

    // Prevent removing the last OWNER
    if (string.Equals(target.Role, UserRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedNewRole, UserRoles.Owner, StringComparison.OrdinalIgnoreCase))
    {
      var owners = await _db.Users.CountAsync(x => x.Role == UserRoles.Owner, ct);
      if (owners <= 1)
      {
        return Conflict(new { error = "LAST_OWNER_CANNOT_BE_REMOVED" });
      }
    }

    if (string.Equals(before, normalizedNewRole, StringComparison.OrdinalIgnoreCase))
      return NoContent(); // no-op

    target.Role = normalizedNewRole;
    target.UpdatedAt = DateTime.UtcNow;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorId,
      TargetUserId = userId,
      Action = "ROLE_CHANGED",
      BeforeRole = before,
      AfterRole = normalizedNewRole,
      CreatedAt = DateTime.UtcNow
    });

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return NoContent();
  }
}
