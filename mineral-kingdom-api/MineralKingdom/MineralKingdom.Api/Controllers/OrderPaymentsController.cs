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
      return Conflict(new { error = "ORDER_NOT_AWAITING_PAYMENT" });
    }
    catch (InvalidOperationException ex) when (
      ex.Message.Equals("AUCTION_SHIPPING_CHOICE_REQUIRED", StringComparison.OrdinalIgnoreCase))
    {
      return Conflict(new { error = "AUCTION_SHIPPING_CHOICE_REQUIRED" });
    }
  }

  [HttpPost("/api/order-payments/{paymentId:guid}/capture")]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
  public async Task<ActionResult<CaptureOrderPaymentResponse>> Capture(
    Guid paymentId,
    CancellationToken ct)
  {
    var userId = GetUserIdOrThrow(User);
    var now = DateTimeOffset.UtcNow;

    var (ok, err, payment) = await _svc.CaptureAsync(paymentId, userId, now, ct);

    if (!ok || payment is null)
    {
      return err switch
      {
        "PAYMENT_NOT_FOUND" => NotFound(new { error = "PAYMENT_NOT_FOUND" }),
        "PROVIDER_CAPTURE_NOT_SUPPORTED" => BadRequest(new { error = "PROVIDER_CAPTURE_NOT_SUPPORTED" }),
        "PROVIDER_CHECKOUT_ID_MISSING" => Conflict(new { error = "PROVIDER_CHECKOUT_ID_MISSING" }),
        "PAYPAL_CAPTURE_FAILED" => BadRequest(new { error = "PAYPAL_CAPTURE_FAILED" }),
        "ORDER_CONFIRMATION_FAILED" => Conflict(new { error = "ORDER_CONFIRMATION_FAILED" }),
        _ => BadRequest(new { error = err ?? "CAPTURE_FAILED" })
      };
    }

    return Ok(new CaptureOrderPaymentResponse(
      PaymentId: payment.Id,
      Provider: payment.Provider,
      PaymentStatus: payment.Status,
      ProviderPaymentId: payment.ProviderPaymentId
    ));
  }

  [HttpGet("/api/order-payments/{paymentId:guid}/confirmation")]
  [Authorize(Policy = AuthorizationPolicies.EmailVerified, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
  public async Task<ActionResult<OrderPaymentConfirmationResponse>> GetConfirmation(
    Guid paymentId,
    CancellationToken ct)
  {
    var userId = GetUserIdOrThrow(User);
    var isPrivileged = User.IsInRole("STAFF") || User.IsInRole("OWNER");

    var ownerUserId = await _svc.GetPaymentOwnerUserIdAsync(paymentId, ct);
    if (ownerUserId is null)
      return NotFound(new { error = "PAYMENT_NOT_FOUND" });

    if (!isPrivileged && ownerUserId.Value != userId)
      return NotFound(new { error = "PAYMENT_NOT_FOUND" });

    var dto = await _svc.GetConfirmationAsync(paymentId, ct);
    if (dto is null)
      return NotFound(new { error = "PAYMENT_NOT_FOUND" });

    return Ok(dto);
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