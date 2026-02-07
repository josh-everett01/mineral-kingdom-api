namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class CheckoutHold
{
  public Guid Id { get; set; }

  public Guid CartId { get; set; }
  public Cart? Cart { get; set; }

  public Guid? UserId { get; set; } // present if member checkout

  public string? GuestEmail { get; set; } // required for guest checkout, used for guest order lookup

  public string Status { get; set; } = "ACTIVE";

  public DateTimeOffset ExpiresAt { get; set; }

  // “first successful payment wins”
  public DateTimeOffset? CompletedAt { get; set; }
  public string? PaymentReference { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? ClientReturnedAt { get; set; }
  public string? ClientReturnReference { get; set; }

}
