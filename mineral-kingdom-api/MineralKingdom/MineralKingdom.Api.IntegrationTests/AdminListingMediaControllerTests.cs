using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AdminListingMediaControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminListingMediaControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Get_Admin_Listing_Media_Returns_Items_With_Primary_First()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Media listing",
      CreatedAt = now,
      UpdatedAt = now
    };

    var secondaryImage = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = "https://img.example/secondary.jpg",
      IsPrimary = false,
      SortOrder = 2,
      OriginalFileName = "secondary.jpg",
      ContentType = "image/jpeg",
      ContentLengthBytes = 2048,
      CreatedAt = now.AddMinutes(-2),
      UpdatedAt = now.AddMinutes(-2)
    };

    var primaryImage = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = "https://img.example/primary.jpg",
      IsPrimary = true,
      SortOrder = 5,
      OriginalFileName = "primary.jpg",
      ContentType = "image/jpeg",
      ContentLengthBytes = 4096,
      CreatedAt = now.AddMinutes(-1),
      UpdatedAt = now.AddMinutes(-1)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Listings.Add(listing);
      db.ListingMedia.AddRange(secondaryImage, primaryImage);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync($"/api/admin/listings/{listing.Id}/media");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var items = await response.Content.ReadFromJsonAsync<List<AdminListingMediaItemDto>>();
    items.Should().NotBeNull();
    items!.Should().HaveCount(2);

    items[0].Id.Should().Be(primaryImage.Id);
    items[0].IsPrimary.Should().BeTrue();
    items[0].MediaType.Should().Be(ListingMediaTypes.Image);
    items[0].Status.Should().Be(ListingMediaStatuses.Ready);

    items[1].Id.Should().Be(secondaryImage.Id);
    items[1].IsPrimary.Should().BeFalse();
  }

  [Fact]
  public async Task Get_Admin_Listing_Media_Returns_NotFound_For_Missing_Listing()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync($"/api/admin/listings/{Guid.NewGuid()}/media");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static void AddAdminHeaders(HttpClient client, User admin)
  {
    client.DefaultRequestHeaders.Remove("X-Test-UserId");
    client.DefaultRequestHeaders.Remove("X-Test-EmailVerified");
    client.DefaultRequestHeaders.Remove("X-Test-Role");

    client.DefaultRequestHeaders.Add("X-Test-UserId", admin.Id.ToString());
    client.DefaultRequestHeaders.Add("X-Test-EmailVerified", "true");
    client.DefaultRequestHeaders.Add("X-Test-Role", admin.Role);
  }

  private static async Task<User> SeedAdminAsync(TestAppFactory factory, DateTimeOffset now)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var admin = new User
    {
      Id = Guid.NewGuid(),
      Email = $"owner-{Guid.NewGuid():N}@example.com",
      PasswordHash = "x",
      EmailVerified = true,
      Role = UserRoles.Owner,
      CreatedAt = now.UtcDateTime,
      UpdatedAt = now.UtcDateTime
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();

    return admin;
  }

  private sealed record AdminListingMediaItemDto(
    Guid Id,
    string MediaType,
    string Status,
    string Url,
    bool IsPrimary,
    int SortOrder,
    string? Caption,
    string? OriginalFileName,
    string? ContentType,
    long? ContentLengthBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
  );
}