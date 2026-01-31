using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ResendVerificationTests : IClassFixture<PostgresContainerFixture>
{
  private readonly HttpClient _client;

  public ResendVerificationTests(PostgresContainerFixture pg)
  {
    var factory = new TestAppFactory(
      host: pg.Host,
      port: pg.Port,
      database: pg.Database,
      username: pg.Username,
      password: pg.Password
    );

    _client = factory.CreateClient();
  }

  private sealed record RegisterRequest(string Email, string Password);

  private sealed record RegisterResponse(
    Guid UserId,
    bool EmailVerified,
    bool VerificationSent,
    string Message,
    string NextStep,
    string? VerificationToken
  );

  private sealed record ResendVerificationRequest(string Email);
  private sealed record ResendVerificationResponse(bool Sent, string? VerificationToken);

  private sealed record VerifyEmailRequest(string Token);

  [Fact]
  public async Task Resend_verification_is_non_enumerating_and_only_returns_token_when_unverified()
  {
    // 1) Unknown email => still 200 OK, Sent=true, token null (no user enumeration)
    var unknownEmail = $"unknown-{Guid.NewGuid():N}@example.com";

    var unknownResp = await _client.PostAsJsonAsync(
  "/api/auth/resend-verification",
  new ResendVerificationRequest(unknownEmail));

    if (!unknownResp.IsSuccessStatusCode)
    {
      var body = await unknownResp.Content.ReadAsStringAsync();
      throw new Exception($"Status={(int)unknownResp.StatusCode} Body={body}");
    }

    unknownResp.StatusCode.Should().Be(HttpStatusCode.OK);


    var unknownBody = await unknownResp.Content.ReadFromJsonAsync<ResendVerificationResponse>();
    unknownBody.Should().NotBeNull();
    unknownBody!.Sent.Should().BeTrue();
    unknownBody.VerificationToken.Should().BeNull();

    // 2) Register a new user (unverified) => token is returned in Testing
    var email = $"resend-{Guid.NewGuid():N}@example.com";
    var password = "P@ssw0rd!!1A"; // >=10, upper/lower/digit

    var regResp = await _client.PostAsJsonAsync(
      "/api/auth/register",
      new RegisterRequest(email, password));

    regResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var regBody = await regResp.Content.ReadFromJsonAsync<RegisterResponse>();
    regBody.Should().NotBeNull();
    regBody!.EmailVerified.Should().BeFalse();
    regBody.VerificationToken.Should().NotBeNullOrWhiteSpace();

    var token1 = regBody.VerificationToken!;

    // Resend for unverified => new token returned (different from first one)
    var resendResp = await _client.PostAsJsonAsync(
      "/api/auth/resend-verification",
      new ResendVerificationRequest(email));

    resendResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var resendBody = await resendResp.Content.ReadFromJsonAsync<ResendVerificationResponse>();
    resendBody.Should().NotBeNull();
    resendBody!.Sent.Should().BeTrue();
    resendBody.VerificationToken.Should().NotBeNullOrWhiteSpace();

    var token2 = resendBody.VerificationToken!;
    token2.Should().NotBe(token1);

    // Verify using the NEW token to prove it is valid
    var verifyResp = await _client.PostAsJsonAsync(
      "/api/auth/verify-email",
      new VerifyEmailRequest(token2));

    verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // 3) Resend after verified => 200 OK, token null
    var resendAfterVerifiedResp = await _client.PostAsJsonAsync(
      "/api/auth/resend-verification",
      new ResendVerificationRequest(email));

    resendAfterVerifiedResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var resendAfterVerifiedBody =
      await resendAfterVerifiedResp.Content.ReadFromJsonAsync<ResendVerificationResponse>();

    resendAfterVerifiedBody.Should().NotBeNull();
    resendAfterVerifiedBody!.Sent.Should().BeTrue();
    resendAfterVerifiedBody.VerificationToken.Should().BeNull();
  }
}
