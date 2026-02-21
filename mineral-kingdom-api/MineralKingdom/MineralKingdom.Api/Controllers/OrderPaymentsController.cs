using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineralKingdom.Api.Security;
using MineralKingdom.Api.Services;
using MineralKingdom.Contracts.Orders;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/payments")]
public sealed class OrderPaymentsController : ControllerBase
{
  private readonly OrderPaymentService _svc;

  public OrderPaymentsController(OrderPaymentService svc) => _svc = svc;

  [HttpPost("start")]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
  public async Task<ActionResult<StartOrderPaymentResponse>> Start(
  Guid orderId,
  [FromBody] StartOrderPaymentRequest req,
  CancellationToken ct)
  {
    var userId = GetUserIdOrThrow(User);

    try
    {
      var res = await _svc.StartAuctionOrderPaymentAsync(orderId, userId, req, ct);
      return Ok(res);
    }
    catch (InvalidOperationException ex) when (
      ex.Message.Equals("ORDER_NOT_AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.Contains("not awaiting payment", StringComparison.OrdinalIgnoreCase))
    {
      // State conflict (already paid, expired, or otherwise not payable)
      return Conflict(new { error = "ORDER_NOT_AWAITING_PAYMENT" });
    }
  }


  private static Guid GetUserIdOrThrow(ClaimsPrincipal user)
  {
    var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue("sub");

    if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id))
      throw new InvalidOperationException("MISSING_SUB_CLAIM");

    return id;
  }
}
