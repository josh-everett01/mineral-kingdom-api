using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Media.Storage;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Media;

public sealed class MediaUploadService
{
  public const int MaxImagesPerListing = 20;
  public const int MaxVideosPerListing = 2;

  public const long MaxImageBytes = 10L * 1024 * 1024;
  public const long MaxVideoBytes = 250L * 1024 * 1024;

  private readonly MineralKingdomDbContext _db;
  private readonly IObjectStorage _storage;
  private readonly MediaStorageOptions _opts;

  public MediaUploadService(
    MineralKingdomDbContext db,
    IObjectStorage storage,
    IOptions<MediaStorageOptions> opts)
  {
    _db = db;
    _storage = storage;
    _opts = opts.Value;
  }

  public sealed record InitiateRequest(
    Guid ListingId,
    string MediaType,
    string FileName,
    string ContentType,
    long ContentLengthBytes,
    bool? IsPrimary,
    int? SortOrder,
    string? Caption
  );

  public sealed record InitiateResult(
    Guid MediaId,
    string StorageKey,
    string UploadUrl,
    Dictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt,
    string PublicUrl
  );

  public async Task<(bool Ok, string? Error, InitiateResult? Result)> InitiateAsync(InitiateRequest req, CancellationToken ct)
  {
    var mt = (req.MediaType ?? "").Trim().ToUpperInvariant();
    if (!ListingMediaTypes.IsValid(mt))
      return (false, "INVALID_MEDIA_TYPE", null);

    if (string.IsNullOrWhiteSpace(req.FileName) || req.FileName.Length > 512)
      return (false, "INVALID_FILE_NAME", null);

    if (string.IsNullOrWhiteSpace(req.ContentType) || req.ContentType.Length > 200)
      return (false, "INVALID_CONTENT_TYPE", null);

    if (req.ContentLengthBytes <= 0)
      return (false, "INVALID_CONTENT_LENGTH", null);

    if (mt == ListingMediaTypes.Video && req.IsPrimary == true)
      return (false, "VIDEO_CANNOT_BE_PRIMARY", null);

    // Size limits
    if (mt == ListingMediaTypes.Image && req.ContentLengthBytes > MaxImageBytes)
      return (false, "FILE_TOO_LARGE", null);

    if (mt == ListingMediaTypes.Video && req.ContentLengthBytes > MaxVideoBytes)
      return (false, "FILE_TOO_LARGE", null);

    var listingExists = await _db.Listings.AsNoTracking().AnyAsync(x => x.Id == req.ListingId, ct);
    if (!listingExists)
      return (false, "LISTING_NOT_FOUND", null);

    // Count limits include UPLOADING + READY (exclude FAILED)
    var imageCount = await _db.ListingMedia.AsNoTracking()
      .CountAsync(x => x.ListingId == req.ListingId
                    && x.MediaType == ListingMediaTypes.Image
                    && x.Status != ListingMediaStatuses.Failed, ct);

    var videoCount = await _db.ListingMedia.AsNoTracking()
      .CountAsync(x => x.ListingId == req.ListingId
                    && x.MediaType == ListingMediaTypes.Video
                    && x.Status != ListingMediaStatuses.Failed, ct);

    if (mt == ListingMediaTypes.Image && imageCount >= MaxImagesPerListing)
      return (false, "IMAGE_LIMIT_EXCEEDED", null);

    if (mt == ListingMediaTypes.Video && videoCount >= MaxVideosPerListing)
      return (false, "VIDEO_LIMIT_EXCEEDED", null);

    // Sort order default: append
    var maxSort = await _db.ListingMedia
      .Where(x => x.ListingId == req.ListingId)
      .Select(x => (int?)x.SortOrder)
      .MaxAsync(ct);

    var sort = req.SortOrder ?? ((maxSort ?? -1) + 1);

    // Generate immutable storage key (server-owned)
    var mediaId = Guid.NewGuid();

    var leaf = SanitizeFileName(req.FileName);
    var ext = Path.GetExtension(leaf);
    var safeExt = string.IsNullOrWhiteSpace(ext) ? "" : ext.ToLowerInvariant();

    // listings/{listingId}/{mediaId}.jpg
    var storageKey = $"listings/{req.ListingId:D}/{mediaId:D}{safeExt}";

    var publicUrl = BuildPublicUrl(_opts.CdnBaseUrl, storageKey);

    var now = DateTimeOffset.UtcNow;

    var row = new ListingMedia
    {
      Id = mediaId,
      ListingId = req.ListingId,

      MediaType = mt,
      Status = ListingMediaStatuses.Uploading,

      StorageKey = storageKey,
      OriginalFileName = leaf,
      ContentType = req.ContentType,
      ContentLengthBytes = req.ContentLengthBytes,

      Url = publicUrl, // stable public URL derived from key

      SortOrder = sort,
      IsPrimary = req.IsPrimary ?? false,
      Caption = req.Caption,

      CreatedAt = now,
      UpdatedAt = now
    };

    _db.ListingMedia.Add(row);
    await _db.SaveChangesAsync(ct);

    var signed = await _storage.CreateSignedPutAsync(
      _opts.Bucket,
      storageKey,
      req.ContentType,
      req.ContentLengthBytes,
      TimeSpan.FromSeconds(_opts.UrlExpirationSeconds),
      ct
    );

    return (true, null, new InitiateResult(
      row.Id,
      storageKey,
      signed.UploadUrl.ToString(),
      new Dictionary<string, string>(signed.RequiredHeaders, StringComparer.OrdinalIgnoreCase),
      signed.ExpiresAt,
      publicUrl
    ));
  }

  public async Task<(bool Ok, string? Error)> CompleteAsync(Guid mediaId, CancellationToken ct)
  {
    var row = await _db.ListingMedia.SingleOrDefaultAsync(x => x.Id == mediaId, ct);
    if (row is null) return (false, "MEDIA_NOT_FOUND");

    if (!string.Equals(row.Status, ListingMediaStatuses.Uploading, StringComparison.OrdinalIgnoreCase))
      return (false, "MEDIA_NOT_UPLOADING");

    if (string.IsNullOrWhiteSpace(row.StorageKey))
      return (false, "STORAGE_KEY_MISSING");

    // NOTE: tests don’t actually upload bytes. If your IObjectStorage implementation
    // is a real S3 client in tests, this will fail. In that case, swap the storage
    // impl in Testing to an in-memory fake that returns true.
    var exists = await _storage.ExistsAsync(_opts.Bucket, row.StorageKey, ct);
    if (!exists) return (false, "OBJECT_NOT_FOUND");

    var now = DateTimeOffset.UtcNow;
    row.Status = ListingMediaStatuses.Ready;
    row.UpdatedAt = now;

    // Ensure we have a primary image among READY images.
    if (row.MediaType == ListingMediaTypes.Image)
    {
      if (row.IsPrimary)
      {
        var others = await _db.ListingMedia
          .Where(x => x.ListingId == row.ListingId
                   && x.MediaType == ListingMediaTypes.Image
                   && x.Id != row.Id
                   && x.IsPrimary)
          .ToListAsync(ct);

        foreach (var o in others) o.IsPrimary = false;
      }
      else
      {
        var hasPrimaryReady = await _db.ListingMedia.AsNoTracking()
          .AnyAsync(x => x.ListingId == row.ListingId
                      && x.MediaType == ListingMediaTypes.Image
                      && x.Status == ListingMediaStatuses.Ready
                      && x.IsPrimary, ct);

        if (!hasPrimaryReady)
          row.IsPrimary = true;
      }
    }
    else
    {
      // Defensive: videos cannot be primary
      row.IsPrimary = false;
    }

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  private static string BuildPublicUrl(string cdnBase, string storageKey)
  {
    cdnBase = (cdnBase ?? "").Trim().TrimEnd('/');
    return $"{cdnBase}/{storageKey}";
  }

  private static string SanitizeFileName(string fileName)
  {
    fileName = fileName.Replace("\\", "/").Trim();
    while (fileName.Contains("//")) fileName = fileName.Replace("//", "/");

    var parts = fileName.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var leaf = parts.Length == 0 ? "file" : parts[^1];

    foreach (var c in Path.GetInvalidFileNameChars())
      leaf = leaf.Replace(c, '_');

    return leaf.Length > 128 ? leaf[..128] : leaf;
  }
}
