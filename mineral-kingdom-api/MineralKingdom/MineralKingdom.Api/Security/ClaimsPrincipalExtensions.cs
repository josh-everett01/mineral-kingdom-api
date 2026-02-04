using System.Security.Claims;

namespace MineralKingdom.Api.Security;

public static class ClaimsPrincipalExtensions
{
  public static Guid GetUserId(this ClaimsPrincipal user)
  {
    // Common claim types we might use depending on token issuance
    var raw =
      user.FindFirstValue(ClaimTypes.NameIdentifier) ??
      user.FindFirstValue("sub") ??
      user.FindFirstValue("nameid");

    if (string.IsNullOrWhiteSpace(raw))
      throw new InvalidOperationException("User id claim missing.");

    if (!Guid.TryParse(raw, out var id))
      throw new InvalidOperationException("User id claim invalid.");

    return id;
  }

  public static string GetRole(this ClaimsPrincipal user)
  {
    return user.FindFirstValue(ClaimTypes.Role)
      ?? user.FindFirstValue("role")
      ?? string.Empty;
  }
}
