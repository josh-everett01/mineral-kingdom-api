using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Infrastructure.Admin.Queues;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/queues")]
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
public sealed class AdminQueuesController : ControllerBase
{
  private readonly AdminQueuesService _queues;

  public AdminQueuesController(AdminQueuesService queues) => _queues = queues;

  [HttpGet("orders-awaiting-payment")]
  public async Task<IActionResult> OrdersAwaitingPayment(CancellationToken ct)
    => Ok(await _queues.GetOrdersAwaitingPaymentAsync(ct));

  [HttpGet("orders-ready-to-fulfill")]
  public async Task<IActionResult> OrdersReadyToFulfill(CancellationToken ct)
    => Ok(await _queues.GetOrdersReadyToFulfillAsync(ct));

  [HttpGet("fulfillment-packed")]
  public async Task<IActionResult> FulfillmentPacked(CancellationToken ct)
    => Ok(await _queues.GetFulfillmentPackedAsync(ct));

  [HttpGet("fulfillment-shipped")]
  public async Task<IActionResult> FulfillmentShipped(CancellationToken ct)
    => Ok(await _queues.GetFulfillmentShippedAsync(ct));

  [HttpGet("open-boxes")]
  public async Task<IActionResult> OpenBoxes(CancellationToken ct)
    => Ok(await _queues.GetOpenBoxesAsync(ct));
}