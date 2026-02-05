namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CartLine
{
  public Guid Id { get; set; }

  public Guid CartId { get; set; }
  public Cart? Cart { get; set; }

  public Guid OfferId { get; set; }

  public int Quantity { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
