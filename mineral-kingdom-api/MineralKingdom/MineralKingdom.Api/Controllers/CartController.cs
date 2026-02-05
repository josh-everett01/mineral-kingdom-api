using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Store;
using System;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly CartService _carts;

  public CartController(MineralKingdomDbContext db, CartService carts)
  {
    _db = db;
    _carts = carts;
  }

  private Guid? TryGetCartIdFromHeader()
  {
    if (!Request.Headers.TryGetValue("X-Cart-Id", out var v)) return null;
    return Guid.TryParse(v.ToString(), out var id) ? id : null;
  }

  private Guid? TryGetUserId()
  {
    // If you already have ClaimsPrincipalExtensions.GetUserId(), use it.
    var raw = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(raw, out var id) ? id : null;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> Get(
  [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
  CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();
    return Ok(CartService.ToDto(cart));
  }

  [HttpPut("lines")]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> UpsertLine(
  [FromBody] UpsertCartLineRequest req,
  [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
  CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);
    Console.WriteLine("Upserting line in cart");
    Console.WriteLine($"Cart ID: {cart.Id}, User ID: {userId}, Offer ID: {req.OfferId}, Quantity: {req.Quantity}");
    var (ok, err) = await _carts.UpsertLineAsync(cart.Id, userId, req.OfferId, req.Quantity, now, ct);
    if (!ok) return BadRequest(new { error = err });

    // IMPORTANT: re-fetch so we return the cart with updated lines
    var refreshed = await _carts.GetCartForResponseAsync(cart.Id, userId, ct);
    if (refreshed is null) return StatusCode(500, new { error = "CART_REFRESH_FAILED" });

    Response.Headers["X-Cart-Id"] = refreshed.Id.ToString();
    return Ok(CartService.ToDto(refreshed));
  }




  [HttpDelete("lines/{offerId:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<CartDto>> RemoveLine(
  [FromRoute] Guid offerId,
  [FromHeader(Name = "X-Cart-Id")] Guid? cartIdHeader,
  CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var userId = User.Identity?.IsAuthenticated == true ? TryGetUserId() : null;

    var cart = await _carts.GetOrCreateAsync(userId, cartIdHeader, now, ct);

    var (ok, err) = await _carts.RemoveLineAsync(cart, offerId, now, ct);
    if (!ok) return BadRequest(new { error = err });

    Response.Headers["X-Cart-Id"] = cart.Id.ToString();
    return Ok(CartService.ToDto(cart));
  }
}
