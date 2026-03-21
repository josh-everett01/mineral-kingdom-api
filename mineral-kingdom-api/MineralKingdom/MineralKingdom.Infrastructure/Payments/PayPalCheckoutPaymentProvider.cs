using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class PayPalCheckoutPaymentProvider : ICheckoutPaymentProvider
{
  private readonly IHttpClientFactory _http;
  private readonly PayPalOptions _pp;
  private readonly PaymentsOptions _payments;

  public PayPalCheckoutPaymentProvider(
    IHttpClientFactory http,
    IOptions<PayPalOptions> pp,
    IOptions<PaymentsOptions> payments)
  {
    _http = http;
    _pp = pp.Value;
    _payments = payments.Value;
  }

  public string Provider => PaymentProviders.PayPal;

  public async Task<CreatePaymentRedirectResult> CreateRedirectAsync(CreatePaymentRedirectRequest request, CancellationToken ct)
  {
    // FAKE mode: no creds needed, return deterministic fake redirect
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
    {
      var fakeOrderId = $"O-FAKE-{request.PaymentId:N}";
      var fakeUrl = $"https://example.invalid/paypal/approve?token={fakeOrderId}";
      return new CreatePaymentRedirectResult(fakeOrderId, fakeUrl);
    }

    // REAL mode: configured-only
    if (string.IsNullOrWhiteSpace(_pp.ClientId) || string.IsNullOrWhiteSpace(_pp.Secret))
      throw new InvalidOperationException("PAYPAL_NOT_CONFIGURED");

    var baseUrl = IsLive(_pp.Environment)
      ? "https://api-m.paypal.com"
      : "https://api-m.sandbox.paypal.com";

    var accessToken = await GetAccessTokenAsync(baseUrl, ct);

    var value = (request.AmountCents / 100.0m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    var createOrderBody = new
    {
      intent = "CAPTURE",
      purchase_units = new[]
      {
        new
        {
          amount = new { currency_code = request.CurrencyCode, value },
          // checkout-hold/store-payment correlation
          custom_id = request.PaymentId.ToString(),
          invoice_id = request.HoldId.ToString()
        }
      },
      application_context = new
      {
        return_url = request.SuccessUrl,
        cancel_url = request.CancelUrl
      }
    };

    var json = JsonSerializer.Serialize(createOrderBody);

    var client = _http.CreateClient("paypal");
    client.BaseAddress = new Uri(baseUrl);

    using var msg = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders");
    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    msg.Content = new StringContent(json, Encoding.UTF8, "application/json");

    using var res = await client.SendAsync(msg, ct);
    var resBody = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
      throw new InvalidOperationException($"PAYPAL_CREATE_ORDER_FAILED:{(int)res.StatusCode}:{resBody}");

    using var doc = JsonDocument.Parse(resBody);
    var root = doc.RootElement;

    var orderId = root.GetProperty("id").GetString();
    if (string.IsNullOrWhiteSpace(orderId))
      throw new InvalidOperationException("PAYPAL_CREATE_ORDER_MISSING_ID");

    string? approveUrl = null;
    if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
    {
      foreach (var l in links.EnumerateArray())
      {
        var rel = l.TryGetProperty("rel", out var r) ? r.GetString() : null;
        if (string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
        {
          approveUrl = l.TryGetProperty("href", out var h) ? h.GetString() : null;
          break;
        }
      }
    }

    if (string.IsNullOrWhiteSpace(approveUrl))
      throw new InvalidOperationException("PAYPAL_CREATE_ORDER_MISSING_APPROVE_URL");

    return new CreatePaymentRedirectResult(orderId, approveUrl!);
  }

  public async Task<CapturePaymentResult> CaptureOrderAsync(string providerCheckoutId, CancellationToken ct)
  {
    if (string.Equals(_payments.Mode, "FAKE", StringComparison.OrdinalIgnoreCase))
    {
      return new CapturePaymentResult(
        ProviderCheckoutId: providerCheckoutId,
        CaptureId: $"CAPTURE-FAKE-{providerCheckoutId}",
        Status: "COMPLETED");
    }

    if (string.IsNullOrWhiteSpace(_pp.ClientId) || string.IsNullOrWhiteSpace(_pp.Secret))
      throw new InvalidOperationException("PAYPAL_NOT_CONFIGURED");

    var baseUrl = IsLive(_pp.Environment)
      ? "https://api-m.paypal.com"
      : "https://api-m.sandbox.paypal.com";

    var accessToken = await GetAccessTokenAsync(baseUrl, ct);

    var client = _http.CreateClient("paypal");
    client.BaseAddress = new Uri(baseUrl);

    using var msg = new HttpRequestMessage(HttpMethod.Post, $"/v2/checkout/orders/{providerCheckoutId}/capture");
    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    msg.Content = new StringContent("{}", Encoding.UTF8, "application/json");

    using var res = await client.SendAsync(msg, ct);
    var resBody = await res.Content.ReadAsStringAsync(ct);

    if (res.StatusCode == HttpStatusCode.UnprocessableEntity &&
        resBody.Contains("ORDER_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase))
    {
      return new CapturePaymentResult(
        ProviderCheckoutId: providerCheckoutId,
        CaptureId: null,
        Status: "ALREADY_CAPTURED");
    }

    if (!res.IsSuccessStatusCode)
      throw new InvalidOperationException($"PAYPAL_CAPTURE_ORDER_FAILED:{(int)res.StatusCode}:{resBody}");

    using var doc = JsonDocument.Parse(resBody);
    var root = doc.RootElement;

    var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
    var captureId = ExtractCaptureId(root);

    if (string.IsNullOrWhiteSpace(status))
      throw new InvalidOperationException("PAYPAL_CAPTURE_ORDER_MISSING_STATUS");

    return new CapturePaymentResult(
      ProviderCheckoutId: providerCheckoutId,
      CaptureId: captureId,
      Status: status!);
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

  private static string? ExtractCaptureId(JsonElement root)
  {
    if (!root.TryGetProperty("purchase_units", out var purchaseUnits) || purchaseUnits.ValueKind != JsonValueKind.Array)
      return null;

    foreach (var pu in purchaseUnits.EnumerateArray())
    {
      if (!pu.TryGetProperty("payments", out var payments) || payments.ValueKind != JsonValueKind.Object)
        continue;

      if (!payments.TryGetProperty("captures", out var captures) || captures.ValueKind != JsonValueKind.Array)
        continue;

      foreach (var capture in captures.EnumerateArray())
      {
        if (capture.TryGetProperty("id", out var id))
          return id.GetString();
      }
    }

    return null;
  }

  private static bool IsLive(string? env)
    => string.Equals(env?.Trim(), "Live", StringComparison.OrdinalIgnoreCase);
}

public sealed record CapturePaymentResult(
  string ProviderCheckoutId,
  string? CaptureId,
  string Status
);