using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
  private readonly AuthService _auth;
  private readonly IWebHostEnvironment _env;
  private readonly IConfiguration _config;

  public AuthController(AuthService auth, IWebHostEnvironment env, IConfiguration config)
  {
    _auth = auth;
    _env = env;
    _config = config;
  }

  public sealed record RegisterRequest(string Email, string Password);
  public sealed record RegisterResponse(
    Guid UserId,
    bool EmailVerified,
    bool VerificationSent,
    string Message,
    string NextStep,
    string? VerificationToken
);


  [HttpPost("register")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
      return BadRequest(new { error = "INVALID_INPUT" });

    try
    {
      // Option A: Link goes to Next.js frontend verify page
      // Configure in appsettings / env as MK_APP:PUBLIC_URL (or MK_APP__PUBLIC_URL)
      var publicUrl = _config["MK_APP:PUBLIC_URL"]?.TrimEnd('/');
      if (string.IsNullOrWhiteSpace(publicUrl))
      {
        // safe fallback for local dev if you haven't set it yet
        publicUrl = $"{Request.Scheme}://{Request.Host}";
      }

      var verificationBaseUrl = $"{publicUrl}/verify-email";

      var (user, rawToken) = await _auth.RegisterAsync(
          req.Email,
          req.Password,
          verificationBaseUrl,
          DateTime.UtcNow,
          ct);

      // Only return raw token in Dev/Testing to support automated tests / local debug
      var includeToken = _env.IsEnvironment("Testing") || _env.IsDevelopment();

      return Created(string.Empty, new RegisterResponse(
          UserId: user.Id,
          EmailVerified: user.EmailVerified,
          VerificationSent: true,
          Message: "Account created. Check your email to verify your address.",
          NextStep: "VERIFY_EMAIL",
          VerificationToken: includeToken ? rawToken : null
      ));
    }
    catch (InvalidOperationException ex) when (ex.Message == "EMAIL_ALREADY_IN_USE")
    {
      return Conflict(new { error = "EMAIL_ALREADY_IN_USE" });
    }
    catch (ValidationException ex)
    {
      return BadRequest(new { error = "WEAK_PASSWORD", message = ex.Message });
    }
    catch
    {
      return StatusCode(500, new { error = "REGISTER_FAILED" });
    }
  }


  public sealed record VerifyEmailRequest(string Token);

  [HttpPost("verify-email")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult> VerifyEmail([FromBody] VerifyEmailRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Token))
    {
      return BadRequest(new { error = "INVALID_INPUT" });
    }

    var ok = await _auth.VerifyEmailAsync(req.Token, DateTime.UtcNow, ct);
    if (!ok)
    {
      return BadRequest(new { error = "INVALID_OR_EXPIRED_TOKEN" });
    }

    return NoContent();
  }

  public sealed record ResendVerificationRequest(string Email);
  public sealed record ResendVerificationResponse(bool Sent, string? VerificationToken);

  [HttpPost("resend-verification")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult<ResendVerificationResponse>> ResendVerification([FromBody] ResendVerificationRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email))
    {
      return BadRequest(new { error = "INVALID_INPUT" });
    }

    var verificationBaseUrl = HttpContext.Request.Scheme + "://" + HttpContext.Request.Host + "/verify-email";
    var (sent, rawToken) = await _auth.ResendVerificationAsync(req.Email, verificationBaseUrl, DateTime.UtcNow, ct);

    var includeToken = _env.IsEnvironment("Testing") || _env.IsDevelopment();
    return Ok(new ResendVerificationResponse(sent, includeToken ? rawToken : null));
  }
}
