namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class OrderPayment
{
  public Guid Id { get; set; }

  public Guid OrderId { get; set; }
  public Order? Order { get; set; }

  // STRIPE | PAYPAL
  public string Provider { get; set; } = "";

  // CREATED | REDIRECTED | SUCCEEDED | FAILED
  public string Status { get; set; } = "CREATED";

  // Provider identifiers
  // Stripe: Checkout Session id; PayPal: Order id
  public string? ProviderCheckoutId { get; set; }

  // Stripe: PaymentIntent id; PayPal: Capture id
  public string? ProviderPaymentId { get; set; }

  public int AmountCents { get; set; }
  public string CurrencyCode { get; set; } = "USD";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
