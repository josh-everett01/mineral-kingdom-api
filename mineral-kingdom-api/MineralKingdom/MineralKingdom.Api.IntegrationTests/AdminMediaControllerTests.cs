using System.Net;
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
public sealed class AdminMediaControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminMediaControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Post_MakePrimary_Sets_Selected_Image_Primary_And_Clears_Previous_Primary()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Primary test listing",
      CreatedAt = now,
      UpdatedAt = now
    };

    var image1 = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = "https://img.example/one.jpg",
      IsPrimary = true,
      SortOrder = 0,
      OriginalFileName = "one.jpg",
      ContentType = "image/jpeg",
      ContentLengthBytes = 1000,
      CreatedAt = now.AddMinutes(-2),
      UpdatedAt = now.AddMinutes(-2)
    };

    var image2 = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = "https://img.example/two.jpg",
      IsPrimary = false,
      SortOrder = 1,
      OriginalFileName = "two.jpg",
      ContentType = "image/jpeg",
      ContentLengthBytes = 1200,
      CreatedAt = now.AddMinutes(-1),
      UpdatedAt = now.AddMinutes(-1)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Listings.Add(listing);
      db.ListingMedia.AddRange(image1, image2);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.PostAsync($"/api/admin/media/{image2.Id}/make-primary", content: null);

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var refreshed = await db.ListingMedia
        .Where(x => x.ListingId == listing.Id)
        .OrderBy(x => x.SortOrder)
        .ToListAsync();

      refreshed.Single(x => x.Id == image1.Id).IsPrimary.Should().BeFalse();
      refreshed.Single(x => x.Id == image2.Id).IsPrimary.Should().BeTrue();
    }
  }

  [Fact]
  public async Task Post_MakePrimary_Rejects_Video()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Video test listing",
      CreatedAt = now,
      UpdatedAt = now
    };

    var video = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Video,
      Status = ListingMediaStatuses.Ready,
      Url = "https://img.example/video.mp4",
      IsPrimary = false,
      SortOrder = 0,
      OriginalFileName = "video.mp4",
      ContentType = "video/mp4",
      ContentLengthBytes = 9999,
      CreatedAt = now,
      UpdatedAt = now
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Listings.Add(listing);
      db.ListingMedia.Add(video);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.PostAsync($"/api/admin/media/{video.Id}/make-primary", content: null);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task Post_MakePrimary_Rejects_NonReady_Media()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Failed image listing",
      CreatedAt = now,
      UpdatedAt = now
    };

    var failedImage = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Failed,
      Url = "https://img.example/failed.jpg",
      IsPrimary = false,
      SortOrder = 0,
      OriginalFileName = "failed.jpg",
      ContentType = "image/jpeg",
      ContentLengthBytes = 1234,
      CreatedAt = now,
      UpdatedAt = now
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Listings.Add(listing);
      db.ListingMedia.Add(failedImage);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.PostAsync($"/api/admin/media/{failedImage.Id}/make-primary", content: null);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task Post_MakePrimary_Returns_NotFound_For_Missing_Media()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.PostAsync($"/api/admin/media/{Guid.NewGuid()}/make-primary", content: null);

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
}