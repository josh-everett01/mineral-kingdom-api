namespace MineralKingdom.Contracts.Store;

public static class PaymentProviders
{
  public const string Fake = "FAKE";
  public const string Stripe = "STRIPE";
  public const string PayPal = "PAYPAL";
}

public static class CheckoutPaymentStatuses
{
  public const string Created = "CREATED";
  public const string Redirected = "REDIRECTED";
  public const string Succeeded = "SUCCEEDED";
  public const string Failed = "FAILED";
}

public sealed record StartPaymentRequest(
  Guid HoldId,
  string Provider,
  string SuccessUrl,
  string CancelUrl
);

public sealed record StartPaymentResponse(
  Guid PaymentId,
  string Provider,
  string RedirectUrl
);

public sealed record PaymentStatusResponse(
  Guid PaymentId,
  string Provider,
  string Status
);
