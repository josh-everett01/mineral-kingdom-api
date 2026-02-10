using Microsoft.Extensions.Logging;
using MineralKingdom.Infrastructure.Auctions;

namespace MineralKingdom.Worker.Jobs;

public sealed class AuctionClosingSweepJob : IJobHandler
{
  private readonly AuctionStateMachineService _sm;
  private readonly ILogger<AuctionClosingSweepJob> _logger;

  public AuctionClosingSweepJob(
    AuctionStateMachineService sm,
    ILogger<AuctionClosingSweepJob> logger)
  {
    _sm = sm;
    _logger = logger;
  }

  // Use the canonical constant
  public string Type => JobTypes.AuctionClosingSweep;

  public async Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var advanced = await _sm.AdvanceDueAuctionsAsync(now, ct);

    _logger.LogInformation(
      "Auction closing sweep advanced {Count} auctions. JobId={JobId}",
      advanced,
      jobId);
  }
}
