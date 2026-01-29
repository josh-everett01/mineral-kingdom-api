using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MineralKingdom.Api.Security;

public static class TestAuthDefaults
{
  public const string Scheme = "Test";

  public const string UserIdHeader = "X-Test-UserId";
  public const string EmailVerifiedHeader = "X-Test-EmailVerified";
}

public sealed class TestAuthOptions : AuthenticationSchemeOptions
{
}

public sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
  public TestAuthHandler(
      IOptionsMonitor<TestAuthOptions> options,
      ILoggerFactory logger,
      UrlEncoder encoder)
      : base(options, logger, encoder)
  {
  }

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(TestAuthDefaults.UserIdHeader, out var userIdValues))
    {
      return Task.FromResult(AuthenticateResult.NoResult());
    }

    var userId = userIdValues.ToString();
    var emailVerified = Request.Headers.TryGetValue(TestAuthDefaults.EmailVerifiedHeader, out var ev)
        ? ev.ToString()
        : "false";

    var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new("email_verified", emailVerified)
        };

    var identity = new ClaimsIdentity(claims, TestAuthDefaults.Scheme);
    var principal = new ClaimsPrincipal(identity);
    var ticket = new AuthenticationTicket(principal, TestAuthDefaults.Scheme);

    return Task.FromResult(AuthenticateResult.Success(ticket));
  }
}
