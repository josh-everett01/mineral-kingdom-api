namespace MineralKingdom.Contracts.Listings;

public static class ListingStatuses
{
  public const string Draft = "DRAFT";
  public const string Published = "PUBLISHED";
  public const string Sold = "SOLD";
  public const string Archived = "ARCHIVED";

  public static bool IsValid(string? status)
    => status is Draft or Published or Sold or Archived;
}
