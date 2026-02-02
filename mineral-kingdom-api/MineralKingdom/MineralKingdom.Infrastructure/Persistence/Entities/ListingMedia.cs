using System.ComponentModel.DataAnnotations;
using MineralKingdom.Contracts.Listings;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class ListingMedia
{
  public Guid Id { get; set; }

  public Guid ListingId { get; set; }
  public Listing Listing { get; set; } = null!;

  // IMAGE | VIDEO
  [MaxLength(10)]
  public string MediaType { get; set; } = ListingMediaTypes.Image;

  // UPLOADING | READY | FAILED | DELETED
  // Defaulting to READY keeps legacy “URL-based” media compatible.
  [MaxLength(20)]
  public string Status { get; set; } = ListingMediaStatuses.Ready;

  // Immutable object key (set for signed uploads). Nullable for legacy URL-only rows.
  [MaxLength(512)]
  public string? StorageKey { get; set; }

  [MaxLength(255)]
  public string? OriginalFileName { get; set; }

  [MaxLength(255)]
  public string? ContentType { get; set; }

  // Required by your new code paths (Publish and upload validations)
  public long ContentLengthBytes { get; set; }

  // Public URL (CDN or direct). Keep as required string for compatibility with existing schema.
  [MaxLength(2000)]
  public string Url { get; set; } = string.Empty;

  public int SortOrder { get; set; }
  public bool IsPrimary { get; set; }

  [MaxLength(500)]
  public string? Caption { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public DateTimeOffset? DeletedAt { get; set; }
}
