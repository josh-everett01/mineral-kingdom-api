using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("db-ping")]
public class DbPingController : ControllerBase
{
    private readonly MineralKingdomDbContext _db;

    public DbPingController(MineralKingdomDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        _db.Pings.Add(new DbPing());
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> Count()
    {
        var count = await _db.Pings.CountAsync();
        return Ok(new { count });
    }
}
