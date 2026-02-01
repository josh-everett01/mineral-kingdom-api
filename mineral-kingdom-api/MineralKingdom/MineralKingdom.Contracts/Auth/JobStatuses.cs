namespace MineralKingdom.Contracts.Auth;

public static class JobStatuses
{
  public const string Pending = "PENDING";
  public const string Running = "RUNNING";
  public const string Succeeded = "SUCCEEDED";
  public const string Failed = "FAILED";
  public const string DeadLetter = "DEAD_LETTER";

  public static bool IsValid(string? status)
  {
    if (string.IsNullOrWhiteSpace(status)) return false;

    var s = status.Trim().ToUpperInvariant();
    return s is Pending or Running or Succeeded or Failed or DeadLetter;
  }
}
