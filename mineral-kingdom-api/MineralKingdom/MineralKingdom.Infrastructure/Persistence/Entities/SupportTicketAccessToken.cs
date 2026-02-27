namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class SupportTicketAccessToken
{
  public Guid Id { get; set; }

  public Guid TicketId { get; set; }
  public SupportTicket Ticket { get; set; } = default!;

  public string TokenHash { get; set; } = default!;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset ExpiresAt { get; set; }
  public DateTimeOffset? UsedAt { get; set; } // informational
}