using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/dev/jobs")]
public sealed class DevJobsController : ControllerBase
{
  private readonly IWebHostEnvironment _env;
  private readonly IJobQueue _queue;

  public DevJobsController(IWebHostEnvironment env, IJobQueue queue)
  {
    _env = env;
    _queue = queue;
  }

  [HttpPost("enqueue")]
  public async Task<ActionResult> Enqueue(CancellationToken ct)
  {
    if (!_env.IsDevelopment())
      return NotFound();

    var id = await _queue.EnqueueAsync(
      type: "DEV_TEST",
      payload: new { msg = "hello from dev" },
      ct: ct);

    return Ok(new { jobId = id });
  }

  [HttpPost("{id:guid}/succeed")]
  public async Task<ActionResult> MarkSucceeded(Guid id, CancellationToken ct)
  {
    if (!_env.IsDevelopment())
      return NotFound();

    var ok = await _queue.MarkSucceededAsync(id, ct);
    return ok ? NoContent() : NotFound();
  }
}
