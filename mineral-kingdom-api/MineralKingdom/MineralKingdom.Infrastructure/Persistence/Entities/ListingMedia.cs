using System.ComponentModel.DataAnnotations;
using MineralKingdom.Contracts.Listings;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class ListingMedia
{
  public Guid Id { get; set; }

  public Guid ListingId { get; set; }
  public Listing Listing { get; set; } = null!;

  [MaxLength(10)]
  public string MediaType { get; set; } = ListingMediaTypes.Image;

  [MaxLength(2000)]
  public string Url { get; set; } = string.Empty;

  public int SortOrder { get; set; }
  public bool IsPrimary { get; set; }

  [MaxLength(500)]
  public string? Caption { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}
