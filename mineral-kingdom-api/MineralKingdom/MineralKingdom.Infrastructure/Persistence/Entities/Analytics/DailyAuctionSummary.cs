namespace MineralKingdom.Infrastructure.Persistence.Entities.Analytics;

public sealed class DailyAuctionSummary
{
  public DateTime Date { get; set; }

  public int AuctionsClosed { get; set; }
  public int AuctionsSold { get; set; }
  public int AuctionsUnsold { get; set; }

  public int? AvgFinalPriceCents { get; set; }
  public double? AvgBidsPerAuction { get; set; }

  public double? ReserveMetRate { get; set; }
  public double? PaymentCompletionRate { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}