namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class OrderLedgerEntry
{
  public Guid Id { get; set; }

  public Guid OrderId { get; set; }
  public Order? Order { get; set; }

  public string EventType { get; set; } = default!; // e.g. PAYMENT_SUCCEEDED, ORDER_CREATED

  public string? DataJson { get; set; } // optional jsonb payload for auditing/debugging

  public DateTimeOffset CreatedAt { get; set; }
}
