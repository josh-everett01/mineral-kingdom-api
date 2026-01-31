namespace MineralKingdom.Contracts.Auth;

public static class UserRoles
{
  public const string User = "USER";
  public const string Staff = "STAFF";
  public const string Owner = "OWNER";

  public static bool IsValid(string role) =>
    string.Equals(role, User, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(role, Staff, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(role, Owner, StringComparison.OrdinalIgnoreCase);
}
