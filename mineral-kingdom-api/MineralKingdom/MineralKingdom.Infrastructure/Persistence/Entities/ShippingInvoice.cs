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

  // Payment provider metadata (set when payment is started / confirmed)
  public string? Provider { get; set; }                 // STRIPE | PAYPAL
  public string? ProviderCheckoutId { get; set; }        // session/order id
  public string? ProviderPaymentId { get; set; }         // payment intent/capture id
  public string? PaymentReference { get; set; }          // optional display/reference

  // Admin override
  public bool IsOverride { get; set; } = false;
  public string? OverrideReason { get; set; }
}