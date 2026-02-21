using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Payments;

public sealed class PayPalWebhookVerifier
{
  private readonly IHttpClientFactory _http;
  private readonly PayPalOptions _pp;

  public PayPalWebhookVerifier(IHttpClientFactory http, IOptions<PayPalOptions> pp)
  {
    _http = http;
    _pp = pp.Value;
  }

  public async Task<bool> VerifyAsync(HttpRequest request, string rawBody, CancellationToken ct)
  {
    // Required headers
    string Get(string name) => request.Headers[name].ToString();

    var transmissionId = Get("PAYPAL-TRANSMISSION-ID");
    var transmissionTime = Get("PAYPAL-TRANSMISSION-TIME");
    var certUrl = Get("PAYPAL-CERT-URL");
    var authAlgo = Get("PAYPAL-AUTH-ALGO");
    var transmissionSig = Get("PAYPAL-TRANSMISSION-SIG");

    if (string.IsNullOrWhiteSpace(transmissionId) ||
        string.IsNullOrWhiteSpace(transmissionTime) ||
        string.IsNullOrWhiteSpace(certUrl) ||
        string.IsNullOrWhiteSpace(authAlgo) ||
        string.IsNullOrWhiteSpace(transmissionSig))
    {
      Console.WriteLine("[PayPalVerify] FAIL missing required headers");
      return false;
    }

    Console.WriteLine($"[PayPalVerify] env={_pp.Environment} webhookId={(string.IsNullOrWhiteSpace(_pp.WebhookId) ? "<missing>" : _pp.WebhookId)}");
    Console.WriteLine($"[PayPalVerify] hasClientId={!string.IsNullOrWhiteSpace(_pp.ClientId)} hasSecret={!string.IsNullOrWhiteSpace(_pp.Secret)}");

    if (string.IsNullOrWhiteSpace(_pp.WebhookId))
    {
      Console.WriteLine("[PayPalVerify] FAIL MK_PAYPAL__WEBHOOK_ID missing/empty");
      return false;
    }

    var baseUrl = string.Equals(_pp.Environment?.Trim(), "Live", StringComparison.OrdinalIgnoreCase)
      ? "https://api-m.paypal.com"
      : "https://api-m.sandbox.paypal.com";

    if (string.IsNullOrWhiteSpace(_pp.ClientId) || string.IsNullOrWhiteSpace(_pp.Secret))
    {
      Console.WriteLine("[PayPalVerify] FAIL MK_PAYPAL__CLIENT_ID or MK_PAYPAL__SECRET missing/empty");
      return false;
    }

    string token;
    try
    {
      token = await GetAccessTokenAsync(baseUrl, ct);
      Console.WriteLine("[PayPalVerify] got access token (prefix) " + token.Substring(0, Math.Min(10, token.Length)));
    }
    catch (Exception ex)
    {
      Console.WriteLine("[PayPalVerify] FAIL getting access token: " + ex.Message);
      return false;
    }

    var verifyBody = new
    {
      auth_algo = authAlgo,
      cert_url = certUrl,
      transmission_id = transmissionId,
      transmission_sig = transmissionSig,
      transmission_time = transmissionTime,
      webhook_id = _pp.WebhookId,
      webhook_event = JsonSerializer.Deserialize<JsonElement>(rawBody)
    };

    var json = JsonSerializer.Serialize(verifyBody);

    var client = _http.CreateClient("paypal");
    client.BaseAddress = new Uri(baseUrl);

    using var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications/verify-webhook-signature");
    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    msg.Content = new StringContent(json, Encoding.UTF8, "application/json");

    using var res = await client.SendAsync(msg, ct);
    var resBody = await res.Content.ReadAsStringAsync(ct);

    if (!res.IsSuccessStatusCode)
    {
      Console.WriteLine($"[PayPalVerify] FAIL verify endpoint {(int)res.StatusCode} body={resBody}");
      return false;
    }

    using var doc = JsonDocument.Parse(resBody);
    var status = doc.RootElement.TryGetProperty("verification_status", out var s) ? s.GetString() : null;

    Console.WriteLine($"[PayPalVerify] verification_status={status}");

    return string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);
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
}
