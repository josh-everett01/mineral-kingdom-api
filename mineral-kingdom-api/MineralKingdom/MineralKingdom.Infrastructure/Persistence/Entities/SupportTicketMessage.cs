namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class SupportTicketMessage
{
  public Guid Id { get; set; }

  public Guid TicketId { get; set; }
  public SupportTicket Ticket { get; set; } = default!;

  public string AuthorType { get; set; } = default!; // CUSTOMER|SUPPORT
  public Guid? AuthorUserId { get; set; }
  public User? AuthorUser { get; set; }

  public string BodyText { get; set; } = default!;
  public bool IsInternalNote { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}