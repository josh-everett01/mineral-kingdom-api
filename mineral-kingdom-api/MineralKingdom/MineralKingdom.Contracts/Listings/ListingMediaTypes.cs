namespace MineralKingdom.Contracts.Listings;

public static class ListingMediaTypes
{
  public const string Image = "IMAGE";
  public const string Video = "VIDEO";

  public static bool IsValid(string? mediaType)
    => mediaType is Image or Video;
}
