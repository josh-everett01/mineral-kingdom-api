namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class PaymentWebhookEvent
{
  public Guid Id { get; set; }

  // STRIPE | PAYPAL
  public string Provider { get; set; } = "";

  // Stripe event id (evt_...), PayPal payload id or transmission id
  public string EventId { get; set; } = "";

  public Guid? CheckoutPaymentId { get; set; }
  public CheckoutPayment? CheckoutPayment { get; set; }

  // Store raw payload for traceability/debugging
  public string PayloadJson { get; set; } = "{}";

  public DateTimeOffset ReceivedAt { get; set; }
  public DateTimeOffset? ProcessedAt { get; set; }
}
