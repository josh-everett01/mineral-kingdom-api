using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.IntegrationTests;

internal static class ListingsCoreTestsPrivate
{
  private sealed record IdResponse(Guid Id);

  public static async Task<User> SeedOwnerAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTime.UtcNow;
    var user = new User
    {
      Id = Guid.NewGuid(),
      Email = $"owner-{Guid.NewGuid():N}@mk.dev",
      PasswordHash = "test",
      Role = UserRoles.Owner,
      EmailVerified = true,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
  }

  public static async Task<Guid> SeedMineralAsync(TestAppFactory factory, string name)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var mineral = new Mineral
    {
      Id = Guid.NewGuid(),
      Name = name,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Minerals.Add(mineral);
    await db.SaveChangesAsync();
    return mineral.Id;
  }

  public static async Task<Guid> CreateDraftListingAsync(HttpClient client, Guid ownerId)
  {
    var create = new HttpRequestMessage(HttpMethod.Post, "/api/admin/listings");
    AddOwnerHeaders(create, ownerId);
    create.Content = JsonContent.Create(new { });

    var created = await client.SendAsync(create);
    created.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await created.Content.ReadFromJsonAsync<IdResponse>();
    body.Should().NotBeNull();
    return body!.Id;
  }

  public static async Task PatchRequiredFieldsAsync(HttpClient client, Guid ownerId, Guid listingId, Guid mineralId)
  {
    var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/listings/{listingId}");
    AddOwnerHeaders(patch, ownerId);

    patch.Content = JsonContent.Create(new
    {
      title = "Nice Quartz",
      description = "Clear quartz point",
      primaryMineralId = mineralId,
      countryCode = "US",
      lengthCm = 12.34m,
      widthCm = 5.50m,
      heightCm = 4.00m
    });

    var resp = await client.SendAsync(patch);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  private static void AddOwnerHeaders(HttpRequestMessage req, Guid userId)
  {
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", UserRoles.Owner);
  }
}
