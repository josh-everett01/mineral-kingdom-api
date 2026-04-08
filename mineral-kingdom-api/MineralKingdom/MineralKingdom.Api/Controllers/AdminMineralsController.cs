using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/minerals")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminMineralsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public AdminMineralsController(MineralKingdomDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<AdminMineralLookupItemDto>>> Lookup(
    [FromQuery] string? query,
    CancellationToken ct)
  {
    var q = (query ?? string.Empty).Trim();

    if (q.Length == 0)
      return Ok(Array.Empty<AdminMineralLookupItemDto>());

    var items = await _db.Minerals
      .AsNoTracking()
      .Where(x => EF.Functions.ILike(x.Name, $"%{q}%"))
      .OrderBy(x => x.Name)
      .Take(20)
      .Select(x => new AdminMineralLookupItemDto(
        x.Id,
        x.Name
      ))
      .ToListAsync(ct);

    return Ok(items);
  }
}