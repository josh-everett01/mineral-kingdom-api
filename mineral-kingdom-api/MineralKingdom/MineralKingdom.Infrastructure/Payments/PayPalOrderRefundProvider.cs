using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class PayPalOrderRefundProvider : IOrderRefundProvider
{
  private readonly MineralKingdomDbContext _db;
  private readonly IHttpClientFactory _http;
  private readonly PaymentsOptions _payments;
  private readonly PayPalOptions _pp;

  public PayPalOrderRefundProvider(
    MineralKingdomDbContext db,
    IHttpClientFactory http,
    IOptions<PaymentsOptions> payments,
    IOptions<PayPalOptions> pp)
  {
    _db = db;
    _http = http;
    _payments = payments.Value;
    _pp = pp.Value;
  }

  public string Provider => PaymentProviders.PayPal;

  public async Task<CreateRefundResult> RefundAsync(
    Guid orderId,
    long amountCents,
    string currencyCode,
    string reason,
    CancellationToken ct)
  {
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
      return new CreateRefundResult($"pp_fake_refund_{orderId:N}_{amountCents}");

    if (string.IsNullOrWhiteSpace(_pp.ClientId) || string.IsNullOrWhiteSpace(_pp.Secret))
      throw new InvalidOperationException("PAYPAL_NOT_CONFIGURED");

    // Find latest paid PayPal payment for this order
    var pay = await _db.OrderPayments.AsNoTracking()
      .Where(p =>
        p.OrderId == orderId &&
        p.Provider == PaymentProviders.PayPal &&
        p.Status == CheckoutPaymentStatuses.Succeeded)
      .OrderByDescending(p => p.CreatedAt)
      .FirstOrDefaultAsync(ct);

    if (pay is null)
      throw new InvalidOperationException("PAYPAL_PAYMENT_NOT_FOUND");

    var captureId = pay.ProviderPaymentId;
    if (string.IsNullOrWhiteSpace(captureId))
      throw new InvalidOperationException("PAYPAL_CAPTURE_ID_MISSING");

    var baseUrl = IsLive(_pp.Environment)
      ? "https://api-m.paypal.com"
      : "https://api-m.sandbox.paypal.com";

    var accessToken = await GetAccessTokenAsync(baseUrl, ct);

    var value = (amountCents / 100.0m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    var note = reason.Length > 255 ? reason[..255] : reason;

    var body = new
    {
      amount = new { value, currency_code = currencyCode },
      note_to_payer = note
    };

    var json = JsonSerializer.Serialize(body);

    var client = _http.CreateClient("paypal");
    client.BaseAddress = new Uri(baseUrl);

    using var req = new HttpRequestMessage(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

    using var res = await client.SendAsync(req, ct);
    var resBody = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
      throw new InvalidOperationException($"PAYPAL_REFUND_FAILED:{(int)res.StatusCode}:{resBody}");

    using var doc = JsonDocument.Parse(resBody);
    var refundId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

    if (string.IsNullOrWhiteSpace(refundId))
      throw new InvalidOperationException("PAYPAL_REFUND_MISSING_ID");

    return new CreateRefundResult(refundId!);
  }

  private async Task<string> GetAccessTokenAsync(string baseUrl, CancellationToken ct)
  {
    var client = _http.CreateClient("paypal");
    client.BaseAddress = new Uri(baseUrl);

    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_pp.ClientId}:{_pp.Secret}"));

    using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    req.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

    using var res = await client.SendAsync(req, ct);
    var body = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
      throw new InvalidOperationException($"PAYPAL_TOKEN_FAILED:{(int)res.StatusCode}:{body}");

    using var doc = JsonDocument.Parse(body);
    var token = doc.RootElement.GetProperty("access_token").GetString();

    if (string.IsNullOrWhiteSpace(token))
      throw new InvalidOperationException("PAYPAL_TOKEN_MISSING");

    return token!;
  }

  private static bool IsLive(string? env)
    => string.Equals(env?.Trim(), "Live", StringComparison.OrdinalIgnoreCase);
}