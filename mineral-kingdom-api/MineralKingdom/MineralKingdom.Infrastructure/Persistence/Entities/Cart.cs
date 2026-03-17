namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Cart
{
  public Guid Id { get; set; }
  public Guid? UserId { get; set; }
  public string Status { get; set; } = "ACTIVE";
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public ICollection<CartLine> Lines { get; set; } = new List<CartLine>();
  public ICollection<CheckoutHold> CheckoutHolds { get; set; } = new List<CheckoutHold>();
  public ICollection<CartNotice> Notices { get; set; } = new List<CartNotice>();
}