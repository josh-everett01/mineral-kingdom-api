namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CheckoutHold
{
  public Guid Id { get; set; }
  public Guid CartId { get; set; }
  public Guid? UserId { get; set; }
  public string? GuestEmail { get; set; }
  public string Status { get; set; } = "ACTIVE";
  public DateTimeOffset ExpiresAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
  public string? PaymentReference { get; set; }
  public DateTimeOffset? ClientReturnedAt { get; set; }
  public string? ClientReturnReference { get; set; }
  public int ExtensionCount { get; set; }

  public Cart Cart { get; set; } = null!;
  public ICollection<CheckoutHoldItem> Items { get; set; } = new List<CheckoutHoldItem>();
}