using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AdminStoreOffersControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminStoreOffersControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Get_Admin_Store_Offers_Returns_List()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Test listing");
    await SeedStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 12_500,
      discountType: DiscountTypes.Flat,
      discountCents: 1_500,
      discountPercentBps: null,
      isActive: true);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/store/offers?listingId={listing.Id}");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<List<AdminStoreOfferListItemDto>>();
    body.Should().NotBeNull();
    body!.Should().HaveCount(1);
    body[0].ListingId.Should().Be(listing.Id);
    body[0].ListingTitle.Should().Be("Test listing");
    body[0].ListingStatus.Should().Be("PUBLISHED");
    body[0].EffectivePriceCents.Should().Be(11_000);
  }

  [Fact]
  public async Task Post_Admin_Store_Offer_Creates_Fixed_Offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Fixed listing");

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/store/offers");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpsertStoreOfferRequest(
      listing.Id,
      PriceCents: 9_900,
      DiscountType: DiscountTypes.None,
      DiscountCents: null,
      DiscountPercentBps: null,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<StoreOfferIdResponse>();
    body.Should().NotBeNull();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var offer = await db.StoreOffers.SingleAsync(x => x.Id == body!.Id);

    offer.ListingId.Should().Be(listing.Id);
    offer.PriceCents.Should().Be(9_900);
    offer.DiscountType.Should().Be(DiscountTypes.None);
    offer.DiscountCents.Should().BeNull();
    offer.DiscountPercentBps.Should().BeNull();
    offer.IsActive.Should().BeTrue();
  }

  [Fact]
  public async Task Post_Admin_Store_Offer_Creates_Flat_Discount_Offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Flat discount listing");

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/store/offers");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpsertStoreOfferRequest(
      listing.Id,
      PriceCents: 12_500,
      DiscountType: DiscountTypes.Flat,
      DiscountCents: 1_500,
      DiscountPercentBps: null,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var id = (await resp.Content.ReadFromJsonAsync<StoreOfferIdResponse>())!.Id;

    var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/store/offers/{id}");
    AddAdminHeaders(getReq, owner.Id, UserRoles.Owner);

    var getResp = await client.SendAsync(getReq);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await getResp.Content.ReadFromJsonAsync<StoreOfferDto>();
    dto.Should().NotBeNull();
    dto!.EffectivePriceCents.Should().Be(11_000);
  }

  [Fact]
  public async Task Post_Admin_Store_Offer_Creates_Percent_Discount_Offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Percent discount listing");

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/store/offers");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpsertStoreOfferRequest(
      listing.Id,
      PriceCents: 12_500,
      DiscountType: DiscountTypes.Percent,
      DiscountCents: null,
      DiscountPercentBps: 1_000,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var id = (await resp.Content.ReadFromJsonAsync<StoreOfferIdResponse>())!.Id;

    var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/store/offers/{id}");
    AddAdminHeaders(getReq, owner.Id, UserRoles.Owner);

    var getResp = await client.SendAsync(getReq);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await getResp.Content.ReadFromJsonAsync<StoreOfferDto>();
    dto.Should().NotBeNull();
    dto!.EffectivePriceCents.Should().Be(11_250);
  }

  [Fact]
  public async Task Post_Admin_Store_Offer_Rejects_Draft_Listing()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "DRAFT", "Draft listing");

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/store/offers");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpsertStoreOfferRequest(
      listing.Id,
      PriceCents: 9_900,
      DiscountType: DiscountTypes.None,
      DiscountCents: null,
      DiscountPercentBps: null,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task Post_Admin_Store_Offer_Rejects_Invalid_Discount_Combination()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Invalid combo listing");

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/store/offers");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpsertStoreOfferRequest(
      listing.Id,
      PriceCents: 10_000,
      DiscountType: DiscountTypes.None,
      DiscountCents: 1_000,
      DiscountPercentBps: null,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Patch_Admin_Store_Offer_Updates_Existing_Offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Patch listing");
    var offer = await SeedStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 10_000,
      discountType: DiscountTypes.None,
      discountCents: null,
      discountPercentBps: null,
      isActive: true);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/store/offers/{offer.Id}");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpdateStoreOfferRequest(
      PriceCents: 12_500,
      DiscountType: DiscountTypes.Percent,
      DiscountCents: null,
      DiscountPercentBps: 2_000,
      IsActive: true,
      StartsAt: null,
      EndsAt: null
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await resp.Content.ReadFromJsonAsync<StoreOfferDto>();
    dto.Should().NotBeNull();
    dto!.PriceCents.Should().Be(12_500);
    dto.DiscountType.Should().Be(DiscountTypes.Percent);
    dto.DiscountPercentBps.Should().Be(2_000);
    dto.EffectivePriceCents.Should().Be(10_000);
  }

  [Fact]
  public async Task Patch_Admin_Store_Offer_Can_Deactivate_Offer()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner);
    var listing = await SeedListingAsync(factory, "PUBLISHED", "Deactivate listing");
    var offer = await SeedStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 10_000,
      discountType: DiscountTypes.None,
      discountCents: null,
      discountPercentBps: null,
      isActive: true);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/store/offers/{offer.Id}");
    AddAdminHeaders(req, owner.Id, UserRoles.Owner);
    req.Content = JsonContent.Create(new UpdateStoreOfferRequest(
      PriceCents: offer.PriceCents,
      DiscountType: offer.DiscountType,
      DiscountCents: offer.DiscountCents,
      DiscountPercentBps: offer.DiscountPercentBps,
      IsActive: false,
      StartsAt: offer.StartsAt,
      EndsAt: offer.EndsAt
    ));

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var updated = await db.StoreOffers.SingleAsync(x => x.Id == offer.Id);
    updated.IsActive.Should().BeFalse();
  }

  [Fact]
  public async Task User_Is_Forbidden_From_Admin_Store_Offer_Endpoints()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var user = await SeedAdminUserAsync(factory, UserRoles.User);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/store/offers");
    AddAdminHeaders(req, user.Id, UserRoles.User);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

  private static async Task<Listing> SeedListingAsync(TestAppFactory factory, string status, string title)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = status,
      Title = title,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);
    await db.SaveChangesAsync();

    return listing;
  }

  private static async Task<StoreOffer> SeedStoreOfferAsync(
    TestAppFactory factory,
    Guid listingId,
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps,
    bool isActive)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      PriceCents = priceCents,
      DiscountType = discountType,
      DiscountCents = discountCents,
      DiscountPercentBps = discountPercentBps,
      IsActive = isActive,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.StoreOffers.Add(offer);
    await db.SaveChangesAsync();

    return offer;
  }
}