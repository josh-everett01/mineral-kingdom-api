using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MineralKingdom.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;


namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
  private readonly AuthService _auth;
  private readonly IWebHostEnvironment _env;
  private readonly IConfiguration _config;
  private readonly PasswordResetService _passwordReset;

  public AuthController(AuthService auth, IWebHostEnvironment env, IConfiguration config, PasswordResetService passwordReset)
  {
    _auth = auth;
    _env = env;
    _config = config;
    _passwordReset = passwordReset;
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
  public async Task<ActionResult<ResendVerificationResponse>> ResendVerification(
  [FromBody] ResendVerificationRequest req,
  CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email))
      return BadRequest(new { error = "INVALID_INPUT" });

    try
    {
      // Option A: Link goes to Next.js verify page
      var publicUrl = _config["MK_APP:PUBLIC_URL"]?.TrimEnd('/');
      if (string.IsNullOrWhiteSpace(publicUrl))
      {
        // safe fallback for local dev if you haven't set it yet
        publicUrl = $"{Request.Scheme}://{Request.Host}";
      }

      var verificationBaseUrl = $"{publicUrl}/verify-email";

      var (sent, rawToken) = await _auth.ResendVerificationAsync(
        req.Email.Trim(),
        verificationBaseUrl,
        DateTime.UtcNow,
        ct);

      // Only return raw token in Dev/Testing to support automated tests / local debug
      var includeToken = _env.IsEnvironment("Testing") || _env.IsDevelopment();

      // NOTE: AuthService already returns (sent: true, rawToken: null) for:
      // - user not found
      // - user already verified
      // This prevents email enumeration.
      return Ok(new ResendVerificationResponse(sent, includeToken ? rawToken : null));
    }
    catch
    {
      return StatusCode(500, new { error = "RESEND_FAILED" });
    }
  }

  public sealed record LoginRequest(string Email, string Password);

  [HttpPost("login")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult<AuthService.AuthTokensResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
      return BadRequest(new { error = "INVALID_INPUT" });

    try
    {
      var tokens = await _auth.LoginAsync(req.Email, req.Password, DateTime.UtcNow, ct);
      return Ok(tokens);
    }
    catch (InvalidOperationException ex) when (ex.Message == "INVALID_CREDENTIALS")
    {
      return Unauthorized(new { error = "INVALID_CREDENTIALS" });
    }
    catch (InvalidOperationException ex) when (ex.Message == "EMAIL_NOT_VERIFIED")
    {
      return StatusCode(403, new { error = "EMAIL_NOT_VERIFIED" });
    }
  }

  public sealed record RefreshRequest(string RefreshToken);

  [HttpPost("refresh")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult<AuthService.AuthTokensResponse>> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.RefreshToken))
      return BadRequest(new { error = "INVALID_INPUT" });

    var result = await _auth.RefreshAsync(req.RefreshToken, DateTime.UtcNow, ct);

    if (!result.Ok)
    {
      return result.ErrorCode switch
      {
        "REFRESH_TOKEN_REUSED" => Unauthorized(new { error = "REFRESH_TOKEN_REUSED" }),
        "INVALID_OR_EXPIRED_REFRESH_TOKEN" => Unauthorized(new { error = "INVALID_OR_EXPIRED_REFRESH_TOKEN" }),
        "INVALID_INPUT" => BadRequest(new { error = "INVALID_INPUT" }),
        "EMAIL_NOT_VERIFIED" => StatusCode(403, new { error = "EMAIL_NOT_VERIFIED" }),
        _ => Unauthorized(new { error = "INVALID_OR_EXPIRED_REFRESH_TOKEN" }),
      };
    }

    return Ok(result.Tokens);
  }

  [HttpGet("me")]
  [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
  public ActionResult GetMe()
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

    var email = User.FindFirstValue(ClaimTypes.Email)
              ?? User.FindFirstValue("email");

    var emailVerifiedRaw = User.FindFirstValue("email_verified");
    var emailVerified = string.Equals(emailVerifiedRaw, "true", StringComparison.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(userId))
      return Unauthorized(new { error = "MISSING_SUB_CLAIM" });

    return Ok(new
    {
      userId,
      email,
      emailVerified
    });
  }

  public sealed record PasswordResetRequest(string Email);
  public sealed record PasswordResetRequestResponse(bool Sent, string? ResetToken);

  [HttpPost("password-reset/request")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult<PasswordResetRequestResponse>> RequestPasswordReset(
    [FromBody] PasswordResetRequest req,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email))
      return BadRequest(new { error = "INVALID_INPUT" });

    var publicUrl = _config["MK_APP:PUBLIC_URL"]?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(publicUrl))
      publicUrl = $"{Request.Scheme}://{Request.Host}";

    // Option A (consistent with verification): frontend page handles token
    var resetBaseUrl = $"{publicUrl}/reset-password";

    var (sent, raw) = await _passwordReset.RequestAsync(req.Email, resetBaseUrl, DateTime.UtcNow, ct);

    var includeToken = _env.IsEnvironment("Testing") || _env.IsDevelopment();
    return Ok(new PasswordResetRequestResponse(sent, includeToken ? raw : null));
  }

  public sealed record PasswordResetConfirmRequest(string Token, string NewPassword);

  [HttpPost("password-reset/confirm")]
  [EnableRateLimiting("auth")]
  public async Task<ActionResult> ConfirmPasswordReset(
    [FromBody] PasswordResetConfirmRequest req,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
      return BadRequest(new { error = "INVALID_INPUT" });

    try
    {
      var (ok, error) = await _passwordReset.ConfirmAsync(req.Token, req.NewPassword, DateTime.UtcNow, ct);

      if (!ok)
      {
        return error switch
        {
          "INVALID_OR_EXPIRED_TOKEN" => BadRequest(new { error = "INVALID_OR_EXPIRED_TOKEN" }),
          "INVALID_INPUT" => BadRequest(new { error = "INVALID_INPUT" }),
          _ => BadRequest(new { error = "INVALID_OR_EXPIRED_TOKEN" })
        };
      }

      return NoContent();
    }
    catch (ValidationException ex)
    {
      return BadRequest(new { error = "WEAK_PASSWORD", message = ex.Message });
    }
  }

}
