using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Security;

public sealed class EmailVerifiedHandler : AuthorizationHandler<EmailVerifiedRequirement>
{
  private readonly MineralKingdomDbContext _db;

  public EmailVerifiedHandler(MineralKingdomDbContext db)
  {
    _db = db;
  }

  protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, EmailVerifiedRequirement requirement)
  {
    if (context.User?.Identity?.IsAuthenticated != true)
    {
      return;
    }

    // Fast-path: trust claim if present (JWT should be issued server-side later).
    var claim = context.User.FindFirst("email_verified");
    if (claim is not null && bool.TryParse(claim.Value, out var v) && v)
    {
      context.Succeed(requirement);
      return;
    }

    // Fallback: check DB using subject claim.
    var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
    if (!Guid.TryParse(sub, out var userId))
    {
      return;
    }

    var verified = await _db.Users.AnyAsync(u => u.Id == userId && u.EmailVerified, CancellationToken.None);
    if (verified)
    {
      context.Succeed(requirement);
    }
  }
}
