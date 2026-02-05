namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CheckoutHold
{
  public Guid Id { get; set; }

  public Guid CartId { get; set; }
  public Cart? Cart { get; set; }

  public Guid? UserId { get; set; } // present if member checkout

  public string Status { get; set; } = "ACTIVE";

  public DateTimeOffset ExpiresAt { get; set; }

  // “first successful payment wins”
  public DateTimeOffset? CompletedAt { get; set; }
  public string? PaymentReference { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
