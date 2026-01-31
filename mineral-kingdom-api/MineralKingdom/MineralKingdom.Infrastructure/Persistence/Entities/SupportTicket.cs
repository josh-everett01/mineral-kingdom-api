namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class SupportTicket
{
  public Guid Id { get; set; }

  public string Email { get; set; } = default!;
  public string Subject { get; set; } = default!;
  public string Category { get; set; } = default!;
  public string Message { get; set; } = default!;

  public Guid? LinkedOrderId { get; set; }
  public Guid? LinkedAuctionId { get; set; }
  public Guid? LinkedShippingInvoiceId { get; set; }
  public Guid? LinkedListingId { get; set; }

  public string Status { get; set; } = "OPEN";
  public DateTime CreatedAt { get; set; }
}
