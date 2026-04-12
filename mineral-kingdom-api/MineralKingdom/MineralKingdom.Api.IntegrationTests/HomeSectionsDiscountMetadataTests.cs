using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Home;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class HomeSectionsDiscountMetadataTests
{
  private readonly PostgresContainerFixture _pg;

  public HomeSectionsDiscountMetadataTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Home_Sections_Featured_Listing_With_Percent_Discount_Returns_Discount_Metadata()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "Featured Percent Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/home-percent.jpg");
    await SeedActiveStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 15_000,
      discountType: "PERCENT",
      discountCents: null,
      discountPercentBps: 2_500);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/home/sections");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<HomeSectionsDto>();
    body.Should().NotBeNull();

    var featured = body!.FeaturedListings.Items.Should().ContainSingle(x => x.ListingId == listing.Id).Subject;
    featured.PriceCents.Should().Be(15_000);
    featured.EffectivePriceCents.Should().Be(11_250);
    featured.DiscountType.Should().Be("PERCENT");
    featured.DiscountCents.Should().BeNull();
    featured.DiscountPercentBps.Should().Be(2_500);
  }

  [Fact]
  public async Task Home_Sections_New_Arrivals_With_Flat_Discount_Returns_Discount_Metadata()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "New Arrival Flat Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/home-flat.jpg");
    await SeedActiveStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 15_000,
      discountType: "FLAT",
      discountCents: 1_000,
      discountPercentBps: null);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/home/sections");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<HomeSectionsDto>();
    body.Should().NotBeNull();

    var arrival = body!.NewArrivals.Items.Should().ContainSingle(x => x.ListingId == listing.Id).Subject;
    arrival.PriceCents.Should().Be(15_000);
    arrival.EffectivePriceCents.Should().Be(14_000);
    arrival.DiscountType.Should().Be("FLAT");
    arrival.DiscountCents.Should().Be(1_000);
    arrival.DiscountPercentBps.Should().BeNull();
  }

  [Fact]
  public async Task Home_Sections_Auction_Items_Leave_Discount_Metadata_Null()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "Auction Home Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/home-auction.jpg");
    await SeedLiveAuctionAsync(factory, listing.Id, currentPriceCents: 9_000);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/home/sections");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<HomeSectionsDto>();
    body.Should().NotBeNull();

    var auction = body!.EndingSoonAuctions.Items.Should().ContainSingle(x => x.ListingId == listing.Id).Subject;
    auction.DiscountType.Should().BeNull();
    auction.DiscountCents.Should().BeNull();
    auction.DiscountPercentBps.Should().BeNull();
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<Listing> SeedPublishedListingAsync(TestAppFactory factory, string title)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Status = ListingStatuses.Published,
      Title = title,
      PublishedAt = now,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Listings.Add(listing);
    await db.SaveChangesAsync();
    return listing;
  }

  private static async Task SeedReadyPrimaryMediaAsync(TestAppFactory factory, Guid listingId, string url)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    db.ListingMedia.Add(new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      MediaType = "IMAGE",
      Status = ListingMediaStatuses.Ready,
      Url = url,
      IsPrimary = true,
      SortOrder = 0,
      CreatedAt = now,
      UpdatedAt = now
    });

    await db.SaveChangesAsync();
  }

  private static async Task SeedActiveStoreOfferAsync(
    TestAppFactory factory,
    Guid listingId,
    int priceCents,
    string discountType,
    int? discountCents,
    int? discountPercentBps)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    db.StoreOffers.Add(new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      PriceCents = priceCents,
      DiscountType = discountType,
      DiscountCents = discountCents,
      DiscountPercentBps = discountPercentBps,
      IsActive = true,
      StartsAt = null,
      EndsAt = null,
      CreatedAt = now,
      UpdatedAt = now
    });

    await db.SaveChangesAsync();
  }

  private static async Task SeedLiveAuctionAsync(
    TestAppFactory factory,
    Guid listingId,
    int currentPriceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTimeOffset.UtcNow;

    db.Auctions.Add(new Auction
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      Status = AuctionStatuses.Live,
      StartingPriceCents = currentPriceCents,
      CurrentPriceCents = currentPriceCents,
      BidCount = 0,
      CloseTime = now.AddHours(2),
      CreatedAt = now,
      UpdatedAt = now
    });

    await db.SaveChangesAsync();
  }
}