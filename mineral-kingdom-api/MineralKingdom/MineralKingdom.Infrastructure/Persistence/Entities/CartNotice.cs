namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CartNotice
{
  public Guid Id { get; set; }
  public Guid CartId { get; set; }
  public string Type { get; set; } = null!;
  public string Message { get; set; } = null!;
  public Guid? OfferId { get; set; }
  public Guid? ListingId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? DismissedAt { get; set; }

  public Cart Cart { get; set; } = null!;
}