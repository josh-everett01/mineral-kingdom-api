using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AdminAuctionsControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminAuctionsControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Post_Admin_Auction_Creates_Draft_Auction()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminAsync(factory, DateTimeOffset.UtcNow, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, ListingStatuses.Published, "Auction listing");

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auctions");
    req.Content = JsonContent.Create(new CreateAuctionRequest(
      ListingId: listing.Id,
      StartingPriceCents: 10_000,
      ReservePriceCents: 12_500,
      QuotedShippingCents: 1_500,
      LaunchMode: AuctionLaunchModes.Draft,
      TimingMode: AuctionTimingModes.Manual,
      DurationHours: null,
      StartTime: DateTimeOffset.UtcNow.AddHours(1),
      CloseTime: DateTimeOffset.UtcNow.AddDays(2)
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await resp.Content.ReadFromJsonAsync<AuctionIdResponse>();
    dto.Should().NotBeNull();

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var auction = await db.Auctions.SingleAsync(x => x.Id == dto!.AuctionId);

    auction.Status.Should().Be(AuctionStatuses.Draft);
    auction.ListingId.Should().Be(listing.Id);
    auction.StartingPriceCents.Should().Be(10_000);
    auction.ReservePriceCents.Should().Be(12_500);
    auction.QuotedShippingCents.Should().Be(1_500);
    auction.StartTime.Should().NotBeNull();
  }

  [Fact]
  public async Task Get_Admin_Auctions_Returns_List()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminAsync(factory, DateTimeOffset.UtcNow, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, ListingStatuses.Published, "Listable auction");
    await SeedAuctionAsync(factory, listing.Id, AuctionStatuses.Draft, 9_000);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/auctions");
    var resp = await client.SendAsync(req);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<List<AdminAuctionListItemDto>>();
    body.Should().NotBeNull();
    body!.Should().ContainSingle(x => x.ListingId == listing.Id);
    body[0].ListingTitle.Should().Be("Listable auction");
  }

  [Fact]
  public async Task Patch_Admin_Auction_Updates_Draft_Fields()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminAsync(factory, DateTimeOffset.UtcNow, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, ListingStatuses.Published, "Patchable auction");
    var auction = await SeedAuctionAsync(factory, listing.Id, AuctionStatuses.Draft, 10_000);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/auctions/{auction.Id}");
    req.Content = JsonContent.Create(new UpdateAuctionRequest(
      StartTime: DateTimeOffset.UtcNow.AddHours(1),
      CloseTime: DateTimeOffset.UtcNow.AddDays(3),
      StartingPriceCents: 12_000,
      ReservePriceCents: 14_000,
      QuotedShippingCents: 2_000
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var updated = await db.Auctions.SingleAsync(x => x.Id == auction.Id);

    updated.StartingPriceCents.Should().Be(12_000);
    updated.ReservePriceCents.Should().Be(14_000);
    updated.QuotedShippingCents.Should().Be(2_000);
  }

  [Fact]
  public async Task Patch_Admin_Auction_Rejects_Non_Draft_Status()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminAsync(factory, DateTimeOffset.UtcNow, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, ListingStatuses.Published, "Live auction");
    var auction = await SeedAuctionAsync(factory, listing.Id, AuctionStatuses.Live, 10_000);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/auctions/{auction.Id}");
    req.Content = JsonContent.Create(new UpdateAuctionRequest(
      StartTime: null,
      CloseTime: DateTimeOffset.UtcNow.AddDays(5),
      StartingPriceCents: null,
      ReservePriceCents: null,
      QuotedShippingCents: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

  private static async Task<User> SeedAdminAsync(TestAppFactory factory, DateTimeOffset now, string role)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var admin = new User
    {
      Id = Guid.NewGuid(),
      Email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
      PasswordHash = "x",
      EmailVerified = true,
      Role = role,
      CreatedAt = now.UtcDateTime,
      UpdatedAt = now.UtcDateTime
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();

    return admin;
  }

  private static async Task<Listing> SeedListingAsync(TestAppFactory factory, string status, string title)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = title,
      Status = status,
      PublishedAt = status == ListingStatuses.Published ? now : null,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);
    await db.SaveChangesAsync();
    return listing;
  }

  private static async Task<Auction> SeedAuctionAsync(
    TestAppFactory factory,
    Guid listingId,
    string status,
    int startingPriceCents)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;
    var auction = new Auction
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      Status = status,
      StartingPriceCents = startingPriceCents,
      CurrentPriceCents = startingPriceCents,
      BidCount = 0,
      ReserveMet = false,
      CloseTime = now.AddDays(2),
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Auctions.Add(auction);
    await db.SaveChangesAsync();
    return auction;
  }

  private sealed record AdminAuctionListItemDto(
    Guid Id,
    Guid ListingId,
    string? ListingTitle,
    string Status,
    int StartingPriceCents,
    int CurrentPriceCents,
    int? ReservePriceCents,
    bool HasReserve,
    bool? ReserveMet,
    int BidCount,
    DateTimeOffset? StartTime,
    DateTimeOffset CloseTime,
    DateTimeOffset? ClosingWindowEnd,
    int? QuotedShippingCents,
    Guid? RelistOfAuctionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
  );
}