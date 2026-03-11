namespace MineralKingdom.Api.Public;

internal static class PublicListingLinks
{
  public static string BuildHref(Guid listingId, string? title)
    => $"/listing/{BuildSlug(title)}-{listingId:D}";

  public static string BuildSlug(string? title)
  {
    if (string.IsNullOrWhiteSpace(title))
      return "listing";

    var chars = title.Trim().ToLowerInvariant();
    var buffer = new List<char>(chars.Length);
    var previousDash = false;

    foreach (var ch in chars)
    {
      if (char.IsLetterOrDigit(ch))
      {
        buffer.Add(ch);
        previousDash = false;
        continue;
      }

      if (previousDash)
        continue;

      buffer.Add('-');
      previousDash = true;
    }

    var slug = new string(buffer.ToArray()).Trim('-');
    return string.IsNullOrWhiteSpace(slug) ? "listing" : slug;
  }
}