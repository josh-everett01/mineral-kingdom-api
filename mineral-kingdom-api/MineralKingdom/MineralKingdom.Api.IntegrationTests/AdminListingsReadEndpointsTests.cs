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
public sealed class AdminListingsReadEndpointsTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminListingsReadEndpointsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_Admin_Listings_Returns_Listings_With_Checklist()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);
    var mineral = SeedMineral(UniqueName("Fluorite"), now);
    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Rainbow Fluorite Tower",
      Description = "A polished fluorite tower specimen.",
      PrimaryMineralId = mineral.Id,
      LocalityDisplay = "Hunan, China",
      CountryCode = "CN",
      LengthCm = 8.5m,
      WidthCm = 2.4m,
      HeightCm = 2.1m,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-5)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Minerals.Add(mineral);
      db.Listings.Add(listing);
      db.ListingMedia.Add(SeedMedia(listing.Id, "https://img.example/rainbow-fluorite.jpg", isPrimary: true, now));

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync("/api/admin/listings");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var payload = await response.Content.ReadFromJsonAsync<List<AdminListingListItemDto>>();
    payload.Should().NotBeNull();

    var row = payload!.Single(x => x.Id == listing.Id);
    row.Title.Should().Be("Rainbow Fluorite Tower");
    row.Status.Should().Be(ListingStatuses.Draft);
    row.PrimaryMineralId.Should().Be(mineral.Id);
    row.PrimaryMineralName.Should().Be(mineral.Name);
    row.LocalityDisplay.Should().Be("Hunan, China");
    row.QuantityAvailable.Should().Be(1);
    row.QuantityTotal.Should().Be(1);
    row.PublishChecklist.CanPublish.Should().BeTrue();
    row.PublishChecklist.Missing.Should().BeEmpty();
  }

  [Fact]
  public async Task Get_Admin_Listing_Detail_Returns_Checklist_And_Media_Summary()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);
    var mineral = SeedMineral(UniqueName("Calcite"), now);
    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = "Golden Calcite Cluster",
      Description = "Cabinet-sized calcite with good luster.",
      PrimaryMineralId = mineral.Id,
      LocalityDisplay = "Elmwood Mine, Tennessee, USA",
      CountryCode = "US",
      AdminArea1 = "Tennessee",
      MineName = "Elmwood Mine",
      LengthCm = 10m,
      WidthCm = 7m,
      HeightCm = 6m,
      WeightGrams = 850,
      SizeClass = "CABINET",
      IsFluorescent = true,
      FluorescenceNotes = "Weak pink under SWUV",
      ConditionNotes = "Minor edgewear on reverse",
      IsLot = false,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-5)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Minerals.Add(mineral);
      db.Listings.Add(listing);
      db.ListingMedia.Add(SeedMedia(listing.Id, "https://img.example/golden-calcite.jpg", isPrimary: true, now));

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync($"/api/admin/listings/{listing.Id}");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await response.Content.ReadFromJsonAsync<AdminListingDetailDto>();
    dto.Should().NotBeNull();

    dto!.Id.Should().Be(listing.Id);
    dto.Title.Should().Be("Golden Calcite Cluster");
    dto.Description.Should().Be("Cabinet-sized calcite with good luster.");
    dto.PrimaryMineralId.Should().Be(mineral.Id);
    dto.PrimaryMineralName.Should().Be(mineral.Name);
    dto.LocalityDisplay.Should().Be("Elmwood Mine, Tennessee, USA");
    dto.CountryCode.Should().Be("US");
    dto.AdminArea1.Should().Be("Tennessee");
    dto.MineName.Should().Be("Elmwood Mine");
    dto.LengthCm.Should().Be(10m);
    dto.WidthCm.Should().Be(7m);
    dto.HeightCm.Should().Be(6m);
    dto.WeightGrams.Should().Be(850);
    dto.SizeClass.Should().Be("CABINET");
    dto.IsFluorescent.Should().BeTrue();
    dto.FluorescenceNotes.Should().Be("Weak pink under SWUV");
    dto.ConditionNotes.Should().Be("Minor edgewear on reverse");
    dto.IsLot.Should().BeFalse();
    dto.QuantityTotal.Should().Be(1);
    dto.QuantityAvailable.Should().Be(1);

    dto.MediaSummary.ReadyImageCount.Should().Be(1);
    dto.MediaSummary.PrimaryReadyImageCount.Should().Be(1);
    dto.MediaSummary.HasPrimaryVideoViolation.Should().BeFalse();

    dto.PublishChecklist.CanPublish.Should().BeTrue();
    dto.PublishChecklist.Missing.Should().BeEmpty();
  }

  [Fact]
  public async Task Get_Admin_Listing_Detail_Returns_Missing_Requirements_For_Incomplete_Draft()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);
    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Draft,
      Title = null,
      Description = null,
      PrimaryMineralId = null,
      LocalityDisplay = null,
      CountryCode = null,
      LengthCm = null,
      WidthCm = null,
      HeightCm = null,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now.AddMinutes(-10),
      UpdatedAt = now.AddMinutes(-5)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Listings.Add(listing);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync($"/api/admin/listings/{listing.Id}");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await response.Content.ReadFromJsonAsync<AdminListingDetailDto>();
    dto.Should().NotBeNull();

    dto!.PublishChecklist.CanPublish.Should().BeFalse();
    dto.PublishChecklist.Missing.Should().Contain(new[]
    {
      "TITLE",
      "DESCRIPTION",
      "PRIMARY_MINERAL",
      "COUNTRY",
      "LENGTH_CM",
      "WIDTH_CM",
      "HEIGHT_CM",
      "IMAGE_REQUIRED"
    });
  }

  [Fact]
  public async Task Get_Admin_Minerals_Lookup_Returns_Matching_Minerals()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    var fluorite = SeedMineral(UniqueName("Fluorite"), now);
    var purpleFluorite = SeedMineral(UniqueName("Purple Fluorite"), now);
    var calcite = SeedMineral(UniqueName("Calcite"), now);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Minerals.AddRange(fluorite, purpleFluorite, calcite);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync("/api/admin/minerals?query=fluor");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var items = await response.Content.ReadFromJsonAsync<List<AdminMineralLookupItemDto>>();
    items.Should().NotBeNull();
    items!.Should().NotBeEmpty();
    items.Should().OnlyContain(x => x.Name.Contains("fluor", StringComparison.OrdinalIgnoreCase));
    items.Select(x => x.Name).Should().Contain(fluorite.Name);
    items.Select(x => x.Name).Should().Contain(purpleFluorite.Name);
    items.Select(x => x.Name).Should().NotContain(calcite.Name);
  }

  [Fact]
  public async Task Get_Admin_Minerals_Lookup_With_Empty_Query_Returns_Empty_List()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var admin = await SeedAdminAsync(factory, now);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, admin);

    var response = await client.GetAsync("/api/admin/minerals?query=");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var items = await response.Content.ReadFromJsonAsync<List<AdminMineralLookupItemDto>>();
    items.Should().NotBeNull();
    items.Should().BeEmpty();
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

  private static Mineral SeedMineral(string name, DateTimeOffset now) =>
    new()
    {
      Id = Guid.NewGuid(),
      Name = name,
      CreatedAt = now,
      UpdatedAt = now
    };

  private static ListingMedia SeedMedia(Guid listingId, string url, bool isPrimary, DateTimeOffset now) =>
    new()
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = url,
      SortOrder = 0,
      IsPrimary = isPrimary,
      ContentLengthBytes = 2048,
      CreatedAt = now,
      UpdatedAt = now
    };

  private static string UniqueName(string baseName)
    => $"{baseName}-{Guid.NewGuid():N}";
}