namespace MineralKingdom.Contracts.Auctions;

public static class AuctionStatuses
{
  public const string Draft = "DRAFT";
  public const string Live = "LIVE";
  public const string Closing = "CLOSING";
  public const string ClosedWaitingOnPayment = "CLOSED_WAITING_ON_PAYMENT";
  public const string ClosedPaid = "CLOSED_PAID";
  public const string ClosedNotSold = "CLOSED_NOT_SOLD";
}
