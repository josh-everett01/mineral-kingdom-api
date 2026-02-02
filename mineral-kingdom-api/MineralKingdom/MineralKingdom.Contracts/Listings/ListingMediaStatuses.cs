namespace MineralKingdom.Contracts.Listings;

public static class ListingMediaStatuses
{
  public const string Uploading = "UPLOADING";
  public const string Ready = "READY";
  public const string Failed = "FAILED";
  public const string Deleted = "DELETED";

  public static bool IsValid(string? status)
  {
    if (string.IsNullOrWhiteSpace(status)) return false;
    status = status.Trim().ToUpperInvariant();
    return status is Uploading or Ready or Failed;
  }
}
