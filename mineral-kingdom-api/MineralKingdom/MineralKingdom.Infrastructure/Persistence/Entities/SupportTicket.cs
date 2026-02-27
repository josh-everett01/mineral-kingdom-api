namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class SupportTicket
{
  public Guid Id { get; set; }

  public string TicketNumber { get; set; } = default!;

  public Guid? CreatedByUserId { get; set; }
  public User? CreatedByUser { get; set; }

  public string? GuestEmail { get; set; }

  public string Subject { get; set; } = default!;
  public string Category { get; set; } = default!;
  public string Priority { get; set; } = "NORMAL";
  public string Status { get; set; } = "OPEN";

  public Guid? AssignedToUserId { get; set; }
  public User? AssignedToUser { get; set; }

  public Guid? LinkedOrderId { get; set; }
  public Guid? LinkedAuctionId { get; set; }
  public Guid? LinkedShippingInvoiceId { get; set; }
  public Guid? LinkedListingId { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? ClosedAt { get; set; }

  public List<SupportTicketMessage> Messages { get; set; } = new();
  public List<SupportTicketAccessToken> AccessTokens { get; set; } = new();
}