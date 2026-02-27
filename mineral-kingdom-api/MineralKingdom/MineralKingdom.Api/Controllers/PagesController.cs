using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Infrastructure.Cms;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/pages")]
public sealed class PagesController : ControllerBase
{
  private readonly CmsPagesService _cms;
  public PagesController(CmsPagesService cms) => _cms = cms;

  [HttpGet("{slug}")]
  public async Task<IActionResult> Get(string slug, CancellationToken ct)
  {
    var dto = await _cms.GetPublishedAsync(slug, ct);
    if (dto is null) return NotFound(new { error = "PAGE_NOT_FOUND" });
    return Ok(dto);
  }
}