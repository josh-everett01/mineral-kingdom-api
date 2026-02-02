namespace MineralKingdom.Infrastructure.Configuration;

public sealed class MediaStorageOptions
{
  // "S3" or "FAKE"
  public string Provider { get; set; } = "FAKE";

  public string Bucket { get; set; } = "mk-media";

  // S3-compatible details (optional for FAKE)
  public string? Region { get; set; }
  public string? ServiceUrl { get; set; }
  public string? AccessKey { get; set; }
  public string? SecretKey { get; set; }

  // Public read base (CDN or app). We'll build Url = CdnBaseUrl + "/" + StorageKey
  public string CdnBaseUrl { get; set; } = "http://localhost:5142/media";

  public int UrlExpirationSeconds { get; set; } = 900; // 15 minutes
}
