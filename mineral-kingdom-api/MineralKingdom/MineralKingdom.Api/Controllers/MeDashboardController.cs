using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Infrastructure.Dashboard;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/me/dashboard")]
[Authorize(Policy = AuthorizationPolicies.EmailVerified)]
public sealed class MeDashboardController : ControllerBase
{
  private readonly DashboardService _dash;

  public MeDashboardController(DashboardService dash) => _dash = dash;

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct)
  {
    var userId = User.GetUserId();
    var now = DateTimeOffset.UtcNow;

    var dto = await _dash.GetMyDashboardAsync(userId, now, ct);
    return Ok(dto);
  }
}