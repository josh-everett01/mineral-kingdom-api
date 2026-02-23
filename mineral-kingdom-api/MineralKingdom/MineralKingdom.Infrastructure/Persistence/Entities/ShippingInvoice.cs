using System;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class ShippingInvoice
{
  public Guid Id { get; set; }

  public Guid FulfillmentGroupId { get; set; }
  public FulfillmentGroup FulfillmentGroup { get; set; } = null!;

  public long AmountCents { get; set; }
  public string CurrencyCode { get; set; } = "USD";

  // UNPAID | PAID | VOID
  public string Status { get; set; } = "UNPAID";

  public DateTimeOffset? PaidAt { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}