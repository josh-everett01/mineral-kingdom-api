using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ListingsBrowseTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public ListingsBrowseTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Browse_returns_public_store_and_auction_listings_with_canonical_hrefs()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var fluoriteName = UniqueName("Fluorite");
    var quartzName = UniqueName("Quartz");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var fluorite = SeedMineral(fluoriteName, now);
      var quartz = SeedMineral(quartzName, now);
      db.Minerals.AddRange(fluorite, quartz);

      var storeListing = SeedPublishedListing("Purple Fluorite Tower", now.AddMinutes(-20), fluorite.Id, "Berbes, Spain", "CABINET", true);
      var auctionListing = SeedPublishedListing("Quartz Cluster", now.AddMinutes(-10), quartz.Id, "Arkansas, USA", "MINIATURE", false);
      var unpublishedListing = SeedDraftListing("Hidden Draft", now, fluorite.Id);

      db.Listings.AddRange(storeListing, auctionListing, unpublishedListing);
      db.ListingMedia.AddRange(
        SeedMedia(storeListing.Id, "https://img.example/store.jpg", true, now),
        SeedMedia(auctionListing.Id, "https://img.example/auction.jpg", true, now),
        SeedMedia(unpublishedListing.Id, "https://img.example/draft.jpg", true, now));

      db.StoreOffers.Add(new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = storeListing.Id,
        PriceCents = 15000,
        DiscountType = DiscountTypes.Flat,
        DiscountCents = 1000,
        IsActive = true,
        StartsAt = now.AddHours(-1),
        EndsAt = now.AddHours(4),
        CreatedAt = now,
        UpdatedAt = now
      });

      db.StoreOffers.Add(new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = unpublishedListing.Id,
        PriceCents = 5000,
        DiscountType = DiscountTypes.None,
        IsActive = true,
        StartsAt = now.AddHours(-1),
        EndsAt = now.AddHours(4),
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = auctionListing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 8000,
        CurrentPriceCents = 9200,
        BidCount = 3,
        StartTime = now.AddHours(-2),
        CloseTime = now.AddHours(2),
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var response = await client.GetAsync("/api/listings");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var dto = await response.Content.ReadFromJsonAsync<ListingBrowseResponseDto>();

    dto.Should().NotBeNull();
    dto!.Items.Should().HaveCount(2);
    dto.Items.Should().OnlyContain(x => x.Href.StartsWith("/listing/"));
    dto.Items.Should().Contain(x => x.ListingType == "STORE" && x.EffectivePriceCents == 14000 && x.IsFluorescent);
    dto.Items.Should().Contain(x => x.ListingType == "AUCTION" && x.CurrentBidCents == 9200);
    dto.Items.Should().NotContain(x => x.Title == "Hidden Draft");
    dto.AvailableFilters.MineralTypes.Should().Contain(new[] { fluoriteName, quartzName });
    dto.AvailableFilters.SizeClasses.Should().Contain(new[] { "CABINET", "MINIATURE" });
  }

  [Fact]
  public async Task Browse_applies_filters_sort_and_pagination()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var fluoriteName = UniqueName("Fluorite");
    var calciteName = UniqueName("Calcite");

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var fluorite = SeedMineral(fluoriteName, now);
      var calcite = SeedMineral(calciteName, now);
      db.Minerals.AddRange(fluorite, calcite);

      var one = SeedPublishedListing("Fluorite One", now.AddMinutes(-30), fluorite.Id, "Durango, Mexico", "CABINET", true);
      var two = SeedPublishedListing("Fluorite Two", now.AddMinutes(-20), fluorite.Id, "Naica, Mexico", "CABINET", true);
      var three = SeedPublishedListing("Calcite Three", now.AddMinutes(-10), calcite.Id, "Elmwood, USA", "MINIATURE", false);

      db.Listings.AddRange(one, two, three);
      db.ListingMedia.AddRange(
        SeedMedia(one.Id, "https://img.example/one.jpg", true, now),
        SeedMedia(two.Id, "https://img.example/two.jpg", true, now),
        SeedMedia(three.Id, "https://img.example/three.jpg", true, now));

      db.StoreOffers.AddRange(
        SeedOffer(one.Id, 30000, now),
        SeedOffer(two.Id, 12000, now),
        SeedOffer(three.Id, 18000, now));

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var response = await client.GetAsync($"/api/listings?listingType=STORE&mineralType={Uri.EscapeDataString(fluoriteName)}&sizeClass=CABINET&fluorescent=true&sort=price_asc&page=1&pageSize=1");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var dto = await response.Content.ReadFromJsonAsync<ListingBrowseResponseDto>();

    dto.Should().NotBeNull();
    dto!.Total.Should().Be(2);
    dto.TotalPages.Should().Be(2);
    dto.Page.Should().Be(1);
    dto.PageSize.Should().Be(1);
    dto.Items.Should().ContainSingle();
    dto.Items[0].Title.Should().Be("Fluorite Two");
    dto.Items[0].PriceCents.Should().Be(12000);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static string UniqueName(string baseName)
    => $"{baseName}-{Guid.NewGuid():N}";

  private static Mineral SeedMineral(string name, DateTimeOffset now) =>
    new()
    {
      Id = Guid.NewGuid(),
      Name = name,
      CreatedAt = now,
      UpdatedAt = now
    };

  private static Listing SeedPublishedListing(
    string title,
    DateTimeOffset publishedAt,
    Guid mineralId,
    string localityDisplay,
    string sizeClass,
    bool isFluorescent) =>
    new()
    {
      Id = Guid.NewGuid(),
      Title = title,
      Description = $"{title} description",
      Status = ListingStatuses.Published,
      PrimaryMineralId = mineralId,
      LocalityDisplay = localityDisplay,
      SizeClass = sizeClass,
      IsFluorescent = isFluorescent,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = publishedAt.AddMinutes(-5),
      UpdatedAt = publishedAt,
      PublishedAt = publishedAt
    };

  private static Listing SeedDraftListing(string title, DateTimeOffset now, Guid mineralId) =>
    new()
    {
      Id = Guid.NewGuid(),
      Title = title,
      Description = $"{title} description",
      Status = ListingStatuses.Draft,
      PrimaryMineralId = mineralId,
      QuantityTotal = 1,
      QuantityAvailable = 1,
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

  private static StoreOffer SeedOffer(Guid listingId, int priceCents, DateTimeOffset now) =>
    new()
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      PriceCents = priceCents,
      DiscountType = DiscountTypes.None,
      IsActive = true,
      StartsAt = now.AddHours(-1),
      EndsAt = now.AddHours(4),
      CreatedAt = now,
      UpdatedAt = now
    };
}