namespace MineralKingdom.Infrastructure.Payments;

public sealed class FakeCheckoutPaymentProvider : ICheckoutPaymentProvider
{
  public string Provider => "FAKE";

  public Task<CreatePaymentRedirectResult> CreateRedirectAsync(CreatePaymentRedirectRequest request, CancellationToken ct)
  {
    var providerCheckoutId = $"fake_{request.PaymentId:N}";
    var url = $"https://example.invalid/checkout/{providerCheckoutId}";
    return Task.FromResult(new CreatePaymentRedirectResult(providerCheckoutId, url));
  }
}
