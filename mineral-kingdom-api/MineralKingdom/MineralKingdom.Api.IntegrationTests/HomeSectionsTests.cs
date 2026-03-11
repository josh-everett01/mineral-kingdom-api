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

public sealed class HomeSectionsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public HomeSectionsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_home_sections_returns_featured_new_and_ending_soon()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing1 = SeedPublishedListing("Featured One", now.AddMinutes(-30));
      var listing2 = SeedPublishedListing("Featured Two", now.AddMinutes(-20));
      var listing3 = SeedPublishedListing("Arrival One", now.AddMinutes(-10));
      var listing4 = SeedPublishedListing("Auction Listing", now.AddMinutes(-5));

      db.Listings.AddRange(listing1, listing2, listing3, listing4);

      db.ListingMedia.AddRange(
        SeedMedia(listing1.Id, "https://img.example/1.jpg", true),
        SeedMedia(listing2.Id, "https://img.example/2.jpg", true),
        SeedMedia(listing3.Id, "https://img.example/3.jpg", true),
        SeedMedia(listing4.Id, "https://img.example/4.jpg", true)
      );

      db.StoreOffers.AddRange(
        SeedOffer(listing1.Id, 1000, now),
        SeedOffer(listing2.Id, 2000, now),
        SeedOffer(listing3.Id, 3000, now)
      );

      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing4.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 500,
        CurrentPriceCents = 900,
        BidCount = 2,
        CloseTime = now.AddMinutes(45),
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var res = await client.GetAsync("/api/home/sections");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<HomeSectionsDto>();
    dto.Should().NotBeNull();

    dto!.FeaturedListings.Title.Should().Be("Featured Listings");
    dto.FeaturedListings.BrowseHref.Should().Be("/shop");
    dto.FeaturedListings.Items.Should().NotBeEmpty();

    dto.NewArrivals.Title.Should().Be("New Arrivals");
    dto.NewArrivals.BrowseHref.Should().Be("/shop");
    dto.NewArrivals.Items.Should().NotBeEmpty();

    dto.EndingSoonAuctions.Title.Should().Be("Auctions Ending Soon");
    dto.EndingSoonAuctions.BrowseHref.Should().Be("/auctions");
    dto.EndingSoonAuctions.Items.Should().ContainSingle();

    dto.EndingSoonAuctions.Items[0].AuctionId.Should().NotBeNull();
    dto.EndingSoonAuctions.Items[0].CurrentBidCents.Should().Be(900);
    dto.EndingSoonAuctions.Items[0].Href.Should().StartWith("/auctions/");
  }

  private static Listing SeedPublishedListing(string title, DateTimeOffset publishedAt) =>
    new()
    {
      Id = Guid.NewGuid(),
      Title = title,
      Description = $"{title} description",
      Status = ListingStatuses.Published,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = publishedAt.AddMinutes(-5),
      UpdatedAt = publishedAt,
      PublishedAt = publishedAt
    };

  private static ListingMedia SeedMedia(Guid listingId, string url, bool isPrimary) =>
    new()
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = url,
      SortOrder = 0,
      IsPrimary = isPrimary,
      ContentLengthBytes = 1234,
      CreatedAt = DateTimeOffset.UtcNow,
      UpdatedAt = DateTimeOffset.UtcNow
    };

  private static StoreOffer SeedOffer(Guid listingId, int priceCents, DateTimeOffset now) =>
    new()
    {
      Id = Guid.NewGuid(),
      ListingId = listingId,
      PriceCents = priceCents,
      DiscountType = MineralKingdom.Contracts.Store.DiscountTypes.None,
      IsActive = true,
      StartsAt = now.AddHours(-1),
      EndsAt = now.AddDays(1),
      CreatedAt = now,
      UpdatedAt = now
    };

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}