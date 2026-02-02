using System.ComponentModel.DataAnnotations;
using MineralKingdom.Contracts.Listings;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Listing
{
  public Guid Id { get; set; }

  [MaxLength(200)]
  public string? Title { get; set; }

  public string? Description { get; set; }

  [MaxLength(30)]
  public string Status { get; set; } = ListingStatuses.Draft;

  // Mineralogy & Locality
  public Guid? PrimaryMineralId { get; set; }
  public Mineral? PrimaryMineral { get; set; }

  [MaxLength(400)]
  public string? LocalityDisplay { get; set; }

  [MaxLength(2)]
  public string? CountryCode { get; set; }

  [MaxLength(120)]
  public string? AdminArea1 { get; set; }

  [MaxLength(120)]
  public string? AdminArea2 { get; set; }

  [MaxLength(200)]
  public string? MineName { get; set; }

  // Dimensions & Weight (cm + grams)
  public decimal? LengthCm { get; set; }
  public decimal? WidthCm { get; set; }
  public decimal? HeightCm { get; set; }
  public int? WeightGrams { get; set; } // optional

  // Classification
  [MaxLength(30)]
  public string? SizeClass { get; set; }

  public bool IsFluorescent { get; set; }

  public string? FluorescenceNotes { get; set; }
  public string? ConditionNotes { get; set; }

  // Inventory
  public bool IsLot { get; set; }
  public int QuantityTotal { get; set; } = 1;
  public int QuantityAvailable { get; set; } = 1;

  // Timestamps
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
  public DateTimeOffset? ArchivedAt { get; set; }

  public List<ListingMedia> Media { get; set; } = new();
}
