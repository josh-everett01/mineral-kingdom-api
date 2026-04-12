using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class ListingsControllerBrowseDiscountMetadataTests
{
  private readonly PostgresContainerFixture _pg;

  public ListingsControllerBrowseDiscountMetadataTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Browse_Store_Listing_With_Percent_Discount_Returns_Discount_Metadata()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "Percent Offer Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/listing-percent.jpg");
    await SeedActiveStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 15_000,
      discountType: "PERCENT",
      discountCents: null,
      discountPercentBps: 2_500);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/listings?listingType=STORE");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<ListingBrowseResponseDto>();
    body.Should().NotBeNull();

    var item = body!.Items.Should().ContainSingle(x => x.Id == listing.Id).Subject;
    item.ListingType.Should().Be("STORE");
    item.PriceCents.Should().Be(15_000);
    item.EffectivePriceCents.Should().Be(11_250);
    item.DiscountType.Should().Be("PERCENT");
    item.DiscountCents.Should().BeNull();
    item.DiscountPercentBps.Should().Be(2_500);
  }

  [Fact]
  public async Task Browse_Store_Listing_With_Flat_Discount_Returns_Discount_Metadata()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "Flat Offer Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/listing-flat.jpg");
    await SeedActiveStoreOfferAsync(
      factory,
      listing.Id,
      priceCents: 15_000,
      discountType: "FLAT",
      discountCents: 1_000,
      discountPercentBps: null);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/listings?listingType=STORE");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<ListingBrowseResponseDto>();
    body.Should().NotBeNull();

    var item = body!.Items.Should().ContainSingle(x => x.Id == listing.Id).Subject;
    item.ListingType.Should().Be("STORE");
    item.PriceCents.Should().Be(15_000);
    item.EffectivePriceCents.Should().Be(14_000);
    item.DiscountType.Should().Be("FLAT");
    item.DiscountCents.Should().Be(1_000);
    item.DiscountPercentBps.Should().BeNull();
  }

  [Fact]
  public async Task Browse_Auction_Listing_Leaves_Discount_Metadata_Null()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var listing = await SeedPublishedListingAsync(factory, "Auction Listing");
    await SeedReadyPrimaryMediaAsync(factory, listing.Id, "https://cdn.example.com/auction.jpg");
    await SeedLiveAuctionAsync(factory, listing.Id, currentPriceCents: 9_500);

    using var client = factory.CreateClient();

    var resp = await client.GetAsync("/api/listings?listingType=AUCTION");
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<ListingBrowseResponseDto>();
    body.Should().NotBeNull();

    var item = body!.Items.Should().ContainSingle(x => x.Id == listing.Id).Subject;
    item.ListingType.Should().Be("AUCTION");
    item.DiscountType.Should().BeNull();
    item.DiscountCents.Should().BeNull();
    item.DiscountPercentBps.Should().BeNull();
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