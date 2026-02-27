namespace MineralKingdom.Infrastructure.Persistence.Entities.Analytics;

public sealed class DailySalesSummary
{
  // Stored as Postgres "date" (UTC date)
  public DateTime Date { get; set; }

  public long GrossSalesCents { get; set; }
  public long NetSalesCents { get; set; } // v1: equals gross for now
  public int OrderCount { get; set; }
  public long AovCents { get; set; }

  public long StoreSalesCents { get; set; }
  public long AuctionSalesCents { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}