namespace MineralKingdom.Infrastructure.Auctions;

public static class BidIncrementTable
{
  // Input and output are cents
  public static int GetIncrementCents(int currentPriceCents)
  {
    if (currentPriceCents < 0) currentPriceCents = 0;

    // whole dollars only
    var dollars = currentPriceCents / 100;

    if (dollars <= 24) return 100;     // $1
    if (dollars <= 49) return 200;     // $2
    if (dollars <= 74) return 300;     // $3
    if (dollars <= 99) return 400;     // $4
    if (dollars <= 499) return 500;    // $5
    if (dollars <= 999) return 1000;   // $10
    return 2500;                       // $25
  }

  public static int MinToBeatCents(int currentPriceCents)
    => checked(currentPriceCents + GetIncrementCents(currentPriceCents));

  public static bool IsWholeDollar(int amountCents) => amountCents % 100 == 0;
}
