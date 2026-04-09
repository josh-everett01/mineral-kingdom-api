using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AdminMineralsControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminMineralsControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Get_Admin_Minerals_Returns_List_And_Supports_Search()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);

    var fluoriteName = $"TestFluorite-{Guid.NewGuid():N}";
    var wulfeniteName = $"TestWulfenite-{Guid.NewGuid():N}";
    var quartzName = $"TestQuartz-{Guid.NewGuid():N}";

    await SeedMineralsAsync(factory,
      (fluoriteName, 2),
      (wulfeniteName, 1),
      (quartzName, 0));

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(
      HttpMethod.Get,
      $"/api/admin/minerals?search={Uri.EscapeDataString("testfluorite")}");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<List<AdminMineralItemResponse>>();
    body.Should().NotBeNull();
    body!.Should().HaveCount(1);

    body[0].Name.Should().Be(fluoriteName);
    body[0].ListingCount.Should().Be(2);
  }

  [Fact]
  public async Task Post_Admin_Minerals_Rejects_Empty_Name()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/minerals");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new { name = "   " });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Post_Admin_Minerals_Rejects_Duplicate_Name_Case_Insensitive()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var duplicateName = $"DuplicateMineral-{Guid.NewGuid():N}";
    await SeedMineralsAsync(factory, (duplicateName, 0));

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/minerals");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new { name = $"  {duplicateName.ToLowerInvariant()}  " });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task User_Is_Forbidden_From_Admin_Minerals_Endpoints()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var user = await SeedAdminUserAsync(factory, UserRoles.User);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/minerals");
    AddAdminHeaders(req, user.Id, UserRoles.User);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Post_Admin_Minerals_Creates_Mineral()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);

    using var client = factory.CreateClient();

    var mineralName = $"Vanadinite-{Guid.NewGuid():N}";

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/minerals");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new { name = mineralName });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<AdminMineralItemResponse>();
    body.Should().NotBeNull();
    body!.Name.Should().Be(mineralName);
    body.ListingCount.Should().Be(0);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var created = await db.Minerals.SingleAsync(x => x.Id == body.Id);
    created.Name.Should().Be(mineralName);
  }

  private static void AddAdminHeaders(HttpRequestMessage req, Guid userId, string role)
  {
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", role);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<User> SeedAdminUserAsync(TestAppFactory factory, string role)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTime.UtcNow;

    var user = new User
    {
      Id = Guid.NewGuid(),
      Email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
      PasswordHash = "x",
      EmailVerified = true,
      Role = role,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return user;
  }

  private static async Task SeedMineralsAsync(TestAppFactory factory, params (string Name, int ListingCount)[] minerals)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    foreach (var mineralSeed in minerals)
    {
      var mineral = new Mineral
      {
        Id = Guid.NewGuid(),
        Name = mineralSeed.Name,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Minerals.Add(mineral);

      for (var i = 0; i < mineralSeed.ListingCount; i++)
      {
        db.Listings.Add(new Listing
        {
          Id = Guid.NewGuid(),
          Status = "DRAFT",
          PrimaryMineralId = mineral.Id,
          Title = $"{mineralSeed.Name} listing {i + 1}",
          CreatedAt = now,
          UpdatedAt = now
        });
      }
    }

    await db.SaveChangesAsync();
  }

  private sealed record AdminMineralItemResponse(
    Guid Id,
    string Name,
    int ListingCount
  );
}