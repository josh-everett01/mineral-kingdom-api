using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Infrastructure.Persistence;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class JwtProtectedEndpointTests
{
  private readonly PostgresContainerFixture _pg;

  public JwtProtectedEndpointTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Me_endpoint_requires_jwt_and_returns_claims()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    using var client = factory.CreateClient();

    // No token => 401
    var noAuth = await client.GetAsync("/api/auth/me");
    noAuth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // Register + verify
    var email = $"test-{Guid.NewGuid():N}@example.com";
    var password = "AwesomePassword!1";

    var regResp = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
    regResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var reg = await regResp.Content.ReadFromJsonAsync<RegisterResponse>();
    reg!.VerificationToken.Should().NotBeNullOrWhiteSpace();

    var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-email", new { token = reg.VerificationToken });
    verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Login => token
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var tokens = await loginResp.Content.ReadFromJsonAsync<AuthTokensResponse>();
    tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();

    // Use token
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

    var meResp = await client.GetAsync("/api/auth/me");
    meResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var me = await meResp.Content.ReadFromJsonAsync<MeResponse>();
    me.Should().NotBeNull();
    me!.Email.Should().Be(email);
    me.EmailVerified.Should().BeTrue();
    me.UserId.Should().NotBeNullOrWhiteSpace();
  }

  private sealed record RegisterResponse(
    Guid UserId,
    bool EmailVerified,
    bool VerificationSent,
    string Message,
    string NextStep,
    string? VerificationToken);

  private sealed record AuthTokensResponse(string AccessToken, int ExpiresIn, string RefreshToken);

  private sealed record MeResponse(string UserId, string Email, bool EmailVerified);
}
