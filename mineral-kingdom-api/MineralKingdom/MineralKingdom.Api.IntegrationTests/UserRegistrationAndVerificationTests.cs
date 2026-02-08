using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Infrastructure.Persistence;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class UserRegistrationAndVerificationTests
{
  private readonly PostgresContainerFixture _pg;

  private const string StrongPassword = "P@ssw0rd!!";

  public UserRegistrationAndVerificationTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Unverified_user_cannot_bid_until_email_verified()
  {
    await using var factory = new TestAppFactory(
        _pg.Host,
        _pg.Port,
        _pg.Database,
        _pg.Username,
        _pg.Password);

    // Ensure schema is applied for this DB
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    using var client = factory.CreateClient();

    // 1) Register
    var registerResp = await client.PostAsJsonAsync("/api/auth/register", new
    {
      email = $"test-{Guid.NewGuid():N}@example.com",
      password = StrongPassword
    });

    registerResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var reg = await registerResp.Content.ReadFromJsonAsync<RegisterResponse>();
    reg.Should().NotBeNull();
    reg!.EmailVerified.Should().BeFalse();
    reg.VerificationToken.Should().NotBeNullOrWhiteSpace();

    reg!.EmailVerified.Should().BeFalse();

    reg.VerificationSent.Should().BeTrue();
    reg.NextStep.Should().Be("VERIFY_EMAIL");
    reg.Message.Should().NotBeNullOrWhiteSpace();

    // Token should exist in Testing env
    reg.VerificationToken.Should().NotBeNullOrWhiteSpace();


    // 2) Bid while unverified => 403
    var auctionId = Guid.NewGuid();

    HttpRequestMessage CreateBidRequest()
    {
      var req = new HttpRequestMessage(HttpMethod.Post, $"/api/auctions/{auctionId}/bids");
      req.Headers.Add("X-Test-UserId", reg.UserId.ToString());
      req.Headers.Add("X-Test-EmailVerified", "false");
      req.Content = JsonContent.Create(new { maxBidCents = 1000, mode = "IMMEDIATE" });
      return req;
    }

    var bidResp1 = await client.SendAsync(CreateBidRequest());
    bidResp1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

    // 3) Verify email => 204
    var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-email", new
    {
      token = reg.VerificationToken
    });
    verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // 4) Bid again => 501
    var bidResp2 = await client.SendAsync(CreateBidRequest());
    bidResp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await bidResp2.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body.Should().NotBeNull();
    body!["error"].Should().Be("AUCTION_NOT_FOUND");

  }

  [Fact]
  public async Task Register_rejects_weak_password()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      await db.Database.MigrateAsync();
    }

    using var client = factory.CreateClient();

    var resp = await client.PostAsJsonAsync("/api/auth/register", new
    {
      email = $"weak-{Guid.NewGuid():N}@example.com",
      password = "short"
    });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Register_rejects_password_shorter_than_10()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var resp = await client.PostAsJsonAsync("/api/auth/register", new
    {
      email = $"weak-{Guid.NewGuid():N}@example.com",
      password = "P@ssw0rd!" // 9 chars
    });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  private sealed record RegisterResponse(
    Guid UserId,
    bool EmailVerified,
    bool VerificationSent,
    string Message,
    string NextStep,
    string VerificationToken);
}
