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
public sealed class ListingsCoreTests
{
  private readonly PostgresContainerFixture _pg;
  public ListingsCoreTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Cannot_publish_without_required_fields()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);

    using var client = factory.CreateClient();

    // create empty draft
    var create = new HttpRequestMessage(HttpMethod.Post, "/api/admin/listings");
    AddOwnerHeaders(create, owner.Id);
    create.Content = JsonContent.Create(new { });

    var created = await client.SendAsync(create);
    created.StatusCode.Should().Be(HttpStatusCode.OK);

    var createdBody = await created.Content.ReadFromJsonAsync<IdResponse>();
    createdBody.Should().NotBeNull();
    var id = createdBody!.Id;

    // publish should fail
    var pub = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{id}/publish");
    AddOwnerHeaders(pub, owner.Id);

    var pubResp = await client.SendAsync(pub);
    pubResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await pubResp.Content.ReadFromJsonAsync<PublishError>();
    body.Should().NotBeNull();
    body!.Error.Should().Be("LISTING_NOT_PUBLISHABLE");
    body.Missing.Should().Contain("TITLE");
    body.Missing.Should().Contain("DESCRIPTION");
    body.Missing.Should().Contain("PRIMARY_MINERAL");
    body.Missing.Should().Contain("COUNTRY");
    body.Missing.Should().Contain("IMAGE_REQUIRED");
  }

  [Fact]
  public async Task Cannot_publish_without_an_image()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);
    var mineralId = await SeedMineralAsync(factory, "Quartz");

    using var client = factory.CreateClient();

    var listingId = await CreateDraftListingAsync(client, owner.Id);

    // fill required fields (except image)
    var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/listings/{listingId}");
    AddOwnerHeaders(patch, owner.Id);
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

    (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // add VIDEO only (does not satisfy publish)
    var addVideo = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/media");
    AddOwnerHeaders(addVideo, owner.Id);
    addVideo.Content = JsonContent.Create(new
    {
      mediaType = "VIDEO",
      url = "https://example.com/video.mp4"
    });

    (await client.SendAsync(addVideo)).StatusCode.Should().Be(HttpStatusCode.OK);

    // publish should fail
    var pub = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/publish");
    AddOwnerHeaders(pub, owner.Id);

    var pubResp = await client.SendAsync(pub);
    pubResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await pubResp.Content.ReadFromJsonAsync<PublishError>();
    body!.Missing.Should().Contain("IMAGE_REQUIRED");
  }

  [Fact]
  public async Task Weight_is_optional_and_dimensions_are_cm()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);
    var mineralId = await SeedMineralAsync(factory, "Fluorite");

    using var client = factory.CreateClient();
    var listingId = await CreateDraftListingAsync(client, owner.Id);

    // required fields + weight omitted
    var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/listings/{listingId}");
    AddOwnerHeaders(patch, owner.Id);
    patch.Content = JsonContent.Create(new
    {
      title = "Fluorite",
      description = "Purple fluorite specimen",
      primaryMineralId = mineralId,
      countryCode = "US",
      lengthCm = 10.25m,
      widthCm = 7.10m,
      heightCm = 6.00m
    });
    (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // add an IMAGE (auto-primary if first)
    var addImg = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/media");
    AddOwnerHeaders(addImg, owner.Id);
    addImg.Content = JsonContent.Create(new
    {
      mediaType = "IMAGE",
      url = "https://example.com/img1.jpg"
    });
    (await client.SendAsync(addImg)).StatusCode.Should().Be(HttpStatusCode.OK);

    // publish should succeed
    var pub = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/publish");
    AddOwnerHeaders(pub, owner.Id);
    (await client.SendAsync(pub)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // public GET should show cm values and weight null
    var get = await client.GetAsync($"/api/listings/{listingId}");
    get.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await get.Content.ReadFromJsonAsync<ListingDto>();
    dto.Should().NotBeNull();

    dto!.LengthCm.Should().Be(10.25m);
    dto.WidthCm.Should().Be(7.10m);
    dto.HeightCm.Should().Be(6.00m);
    dto.WeightGrams.Should().BeNull();
  }

  [Fact]
  public async Task Cannot_publish_archived_listing()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);
    var mineralId = await SeedMineralAsync(factory, "Calcite");

    using var client = factory.CreateClient();
    var listingId = await CreateDraftListingAsync(client, owner.Id);

    // set required fields
    var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/listings/{listingId}");
    AddOwnerHeaders(patch, owner.Id);
    patch.Content = JsonContent.Create(new
    {
      title = "Calcite",
      description = "Orange calcite chunk",
      primaryMineralId = mineralId,
      countryCode = "US",
      lengthCm = 3.00m,
      widthCm = 3.00m,
      heightCm = 3.00m
    });
    (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // add image
    var addImg = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/media");
    AddOwnerHeaders(addImg, owner.Id);
    addImg.Content = JsonContent.Create(new { mediaType = "IMAGE", url = "https://example.com/calcite.jpg" });
    (await client.SendAsync(addImg)).StatusCode.Should().Be(HttpStatusCode.OK);

    // publish
    var pub = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/publish");
    AddOwnerHeaders(pub, owner.Id);
    (await client.SendAsync(pub)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // archive
    var arch = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/archive");
    AddOwnerHeaders(arch, owner.Id);
    (await client.SendAsync(arch)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // publish again => conflict
    var pub2 = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/publish");
    AddOwnerHeaders(pub2, owner.Id);

    (await client.SendAsync(pub2)).StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  private static void AddOwnerHeaders(HttpRequestMessage req, Guid userId)
  {
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", UserRoles.Owner);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<User> SeedOwnerAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTime.UtcNow;
    var owner = new User
    {
      Id = Guid.NewGuid(),
      Email = $"owner-{Guid.NewGuid():N}@x.com",
      PasswordHash = "x",
      EmailVerified = true,
      Role = UserRoles.Owner,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Users.Add(owner);
    await db.SaveChangesAsync();
    return owner;
  }

  private static async Task<Guid> SeedMineralAsync(TestAppFactory factory, string name)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var m = new Mineral
    {
      Id = Guid.NewGuid(),
      Name = name,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Minerals.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private static async Task<Guid> CreateDraftListingAsync(HttpClient client, Guid ownerId)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/listings");
    req.Headers.Add("X-Test-UserId", ownerId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", UserRoles.Owner);
    req.Content = JsonContent.Create(new { });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<IdResponse>();
    body.Should().NotBeNull();
    return body!.Id;
  }

  private sealed record IdResponse(Guid Id);
  private sealed record PublishError(string Error, List<string> Missing);

  private sealed record ListingDto(
    Guid Id,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    int? WeightGrams
  );
}
