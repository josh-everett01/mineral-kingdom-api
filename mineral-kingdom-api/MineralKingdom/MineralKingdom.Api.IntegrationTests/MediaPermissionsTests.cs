using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class MediaPermissionsTests
{
  private readonly PostgresContainerFixture _pg;
  public MediaPermissionsTests(PostgresContainerFixture pg) => _pg = pg;

  private sealed record IdResponse(Guid Id);

  private sealed record InitiateResp(
    Guid MediaId,
    string StorageKey,
    string UploadUrl,
    Dictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt,
    string PublicUrl
  );

  [Theory]
  [InlineData(AuctionStatuses.Live)]
  [InlineData(AuctionStatuses.Closing)]
  public async Task Deleting_media_is_blocked_when_auction_is_live_or_closing(string auctionStatus)
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);
    var mineralId = await SeedMineralAsync(factory, "Quartz");

    using var client = factory.CreateClient();

    // Create draft listing
    var listingId = await CreateDraftListingAsync(client, owner.Id);

    // Patch required fields
    await PatchRequiredFieldsAsync(client, owner.Id, listingId, mineralId);

    // Initiate + complete an IMAGE
    var mediaId = await InitiateAndCompleteAsync(client, owner.Id, listingId,
      mediaType: "IMAGE",
      fileName: "img.jpg",
      contentType: "image/jpeg",
      contentLengthBytes: 1024);

    // Seed auction in LIVE/CLOSING
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Status = auctionStatus,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    // Attempt delete => 409 Conflict
    var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/media/{mediaId}");
    AddOwnerHeaders(del, owner.Id);

    var resp = await client.SendAsync(del);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    body.Should().NotBeNull();
    body!["error"].Should().Be("MEDIA_DELETE_BLOCKED_AUCTION_ACTIVE");
  }

  [Fact]
  public async Task Deleting_media_is_allowed_when_no_active_auction()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedOwnerAsync(factory);
    var mineralId = await SeedMineralAsync(factory, "Fluorite");

    using var client = factory.CreateClient();

    var listingId = await CreateDraftListingAsync(client, owner.Id);
    await PatchRequiredFieldsAsync(client, owner.Id, listingId, mineralId);

    var mediaId = await InitiateAndCompleteAsync(client, owner.Id, listingId,
      mediaType: "IMAGE",
      fileName: "img.jpg",
      contentType: "image/jpeg",
      contentLengthBytes: 1024);

    var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/media/{mediaId}");
    AddOwnerHeaders(del, owner.Id);

    (await client.SendAsync(del)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var row = await db.ListingMedia.AsNoTracking().SingleAsync(x => x.Id == mediaId);
      row.Status.Should().Be("DELETED");
      row.DeletedAt.Should().NotBeNull();
    }
  }

  // ---------------- helpers ----------------

  private static void AddOwnerHeaders(HttpRequestMessage req, Guid userId)
  {
    req.Headers.Add("X-Test-UserId", userId.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", UserRoles.Owner);
  }

  private static async Task<Guid> CreateDraftListingAsync(HttpClient client, Guid ownerId)
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/listings");
    AddOwnerHeaders(req, ownerId);
    req.Content = JsonContent.Create(new { });

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<IdResponse>();
    body.Should().NotBeNull();
    return body!.Id;
  }

  private static async Task PatchRequiredFieldsAsync(HttpClient client, Guid ownerId, Guid listingId, Guid mineralId)
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

    (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  private static async Task<Guid> InitiateAndCompleteAsync(
    HttpClient client,
    Guid ownerId,
    Guid listingId,
    string mediaType,
    string fileName,
    string contentType,
    long contentLengthBytes)
  {
    var init = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/listings/{listingId}/media/initiate");
    AddOwnerHeaders(init, ownerId);
    init.Content = JsonContent.Create(new
    {
      mediaType,
      fileName,
      contentType,
      contentLengthBytes
    });

    var initResp = await client.SendAsync(init);
    initResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await initResp.Content.ReadFromJsonAsync<InitiateResp>();
    body.Should().NotBeNull();

    var complete = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/media/{body!.MediaId}/complete");
    AddOwnerHeaders(complete, ownerId);

    var compResp = await client.SendAsync(complete);
    compResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    return body.MediaId;
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
      Email = $"owner-{Guid.NewGuid():N}@mk.dev",
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

    // Fix: minerals.Name is UNIQUE, so reuse if already seeded by other tests
    var existing = await db.Minerals.AsNoTracking()
      .Where(x => x.Name == name)
      .Select(x => new { x.Id })
      .FirstOrDefaultAsync();

    if (existing is not null)
      return existing.Id;

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
}
