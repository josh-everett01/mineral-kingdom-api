using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AuthJwtAndRefreshTokenTests
{
  private readonly PostgresContainerFixture _pg;

  public AuthJwtAndRefreshTokenTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Login_returns_access_and_refresh_and_refresh_is_hashed_in_db()
  {
    await using var factory = CreateFactory();
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    // Register + verify so login is allowed
    var (email, password, verificationToken) = await RegisterAndVerifyAsync(client);

    // Login
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
    {
      email,
      password
    });

    loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var tokens = await loginResp.Content.ReadFromJsonAsync<AuthTokensResponse>();
    tokens.Should().NotBeNull();
    tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
    tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
    tokens.ExpiresIn.Should().BeGreaterThan(0);

    // Assert refresh token is stored hashed (not raw) in DB
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var refreshHash = RefreshTokenService.ComputeTokenHash(tokens.RefreshToken);

    var row = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == refreshHash);
    row.Should().NotBeNull("refresh token hash should be stored in DB");
  }

  [Fact]
  public async Task Refresh_rotates_token_and_marks_old_as_used()
  {
    await using var factory = CreateFactory();
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    var (email, password, _) = await RegisterAndVerifyAsync(client);

    // Login -> get refresh token #1
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var loginTokens = await loginResp.Content.ReadFromJsonAsync<AuthTokensResponse>();
    loginTokens.Should().NotBeNull();

    var oldRefresh = loginTokens!.RefreshToken;

    // Refresh -> should rotate to refresh token #2
    var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
    refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var newTokens = await refreshResp.Content.ReadFromJsonAsync<AuthTokensResponse>();
    newTokens.Should().NotBeNull();
    newTokens!.RefreshToken.Should().NotBeNullOrWhiteSpace();
    newTokens.RefreshToken.Should().NotBe(oldRefresh);

    // DB asserts: old token used, replacedBy set
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var oldHash = RefreshTokenService.ComputeTokenHash(oldRefresh);
    var newHash = RefreshTokenService.ComputeTokenHash(newTokens.RefreshToken);

    var oldRow = await db.RefreshTokens.SingleAsync(x => x.TokenHash == oldHash);
    var newRow = await db.RefreshTokens.SingleAsync(x => x.TokenHash == newHash);

    oldRow.UsedAt.Should().NotBeNull("old refresh token should be marked used on rotation");
    oldRow.ReplacedByTokenHash.Should().Be(newRow.TokenHash);
    oldRow.RevokedAt.Should().BeNull("normal rotation should not revoke the old token");
  }

  [Fact]
  public async Task Reused_refresh_token_is_rejected_and_revokes_all_user_tokens()
  {
    await using var factory = CreateFactory();
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    var (email, password, _) = await RegisterAndVerifyAsync(client);

    // Login -> refresh token #1
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var loginTokens = await loginResp.Content.ReadFromJsonAsync<AuthTokensResponse>();
    loginTokens.Should().NotBeNull();
    var oldRefresh = loginTokens!.RefreshToken;

    // Refresh once -> rotates to token #2, marks token #1 as used
    var refreshResp1 = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
    refreshResp1.StatusCode.Should().Be(HttpStatusCode.OK);

    var rotated = await refreshResp1.Content.ReadFromJsonAsync<AuthTokensResponse>();
    rotated.Should().NotBeNull();
    var newRefresh = rotated!.RefreshToken;

    // Reuse old token #1 -> should be rejected and revoke all
    var reuseResp = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
    reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    var err = await reuseResp.Content.ReadFromJsonAsync<ErrorResponse>();
    err.Should().NotBeNull();
    err!.Error.Should().Be("REFRESH_TOKEN_REUSED");

    // DB asserts: ALL refresh tokens for that user revoked
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var user = await db.Users.SingleAsync(u => u.Email == email);
    var tokens = await db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();

    tokens.Should().NotBeEmpty();
    tokens.Should().OnlyContain(t => t.RevokedAt != null, "token reuse should revoke all user refresh tokens");

    // Also ensure the rotated token is now revoked too (matches your manual test output)
    var newHash = RefreshTokenService.ComputeTokenHash(newRefresh);
    var newRow = tokens.Single(t => t.TokenHash == newHash);
    newRow.RevokedAt.Should().NotBeNull("revoke-all should include the currently active token");
  }

  // -----------------------
  // Helpers
  // -----------------------

  private TestAppFactory CreateFactory()
    => new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<(string email, string password, string verificationToken)> RegisterAndVerifyAsync(HttpClient client)
  {
    var email = $"test-{Guid.NewGuid():N}@example.com";
    var password = "AwesomePassword!1";

    var registerResp = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
    registerResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var reg = await registerResp.Content.ReadFromJsonAsync<RegisterResponse>();
    reg.Should().NotBeNull();
    reg!.VerificationToken.Should().NotBeNullOrWhiteSpace("Testing env should return token for automation");

    var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-email", new { token = reg.VerificationToken });
    verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    return (email, password, reg.VerificationToken!);
  }

  private sealed record RegisterResponse(
    Guid UserId,
    bool EmailVerified,
    bool VerificationSent,
    string Message,
    string NextStep,
    string? VerificationToken);

  private sealed record AuthTokensResponse(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken);

  private sealed record ErrorResponse(string Error);
}
