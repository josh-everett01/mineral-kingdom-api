using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Auth;

namespace MineralKingdom.Api.Security;

public static class TestAuthDefaults
{
  public const string Scheme = "Test";

  public const string UserIdHeader = "X-Test-UserId";
  public const string EmailVerifiedHeader = "X-Test-EmailVerified";
  public const string RoleHeader = "X-Test-Role";

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

    var userId = userIdValues.ToString().Trim();

    var emailVerified = Request.Headers.TryGetValue(TestAuthDefaults.EmailVerifiedHeader, out var ev)
      ? ev.ToString().Trim().ToLowerInvariant()
      : "false";

    var rawRole = Request.Headers.TryGetValue(TestAuthDefaults.RoleHeader, out var roleHeader)
      ? roleHeader.ToString()
      : UserRoles.User;

    var normalizedRole = rawRole.Trim().ToUpperInvariant();

    // Guard against garbage roles: default to USER
    var role = UserRoles.IsValid(normalizedRole) ? normalizedRole : UserRoles.User;

    var claims = new List<Claim>
  {
    new(ClaimTypes.NameIdentifier, userId),
    new("sub", userId),
    new("email_verified", emailVerified),
    new(ClaimTypes.Role, role)
  };

    var identity = new ClaimsIdentity(claims, TestAuthDefaults.Scheme);
    var principal = new ClaimsPrincipal(identity);
    var ticket = new AuthenticationTicket(principal, TestAuthDefaults.Scheme);

    if (!Guid.TryParse(userId, out _))
      return Task.FromResult(AuthenticateResult.Fail("Invalid X-Test-UserId header"));


    return Task.FromResult(AuthenticateResult.Success(ticket));
  }
}
