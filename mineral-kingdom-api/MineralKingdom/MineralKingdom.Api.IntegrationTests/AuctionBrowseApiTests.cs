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

public sealed class AuctionBrowseApiTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionBrowseApiTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_auctions_returns_public_live_closing_and_scheduled_auctions()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing1 = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Rainbow Fluorite Tower",
        Description = "Test",
        Status = ListingStatuses.Published,
        LocalityDisplay = "Inner Mongolia, China",
        SizeClass = "CABINET",
        IsFluorescent = true,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      var listing2 = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Amethyst Cathedral",
        Description = "Test",
        Status = ListingStatuses.Published,
        LocalityDisplay = "Artigas, Uruguay",
        SizeClass = "CABINET",
        IsFluorescent = false,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      var listing3 = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Upcoming Vanadinite",
        Description = "Test",
        Status = ListingStatuses.Published,
        LocalityDisplay = "Mibladen, Morocco",
        SizeClass = "MINIATURE",
        IsFluorescent = false,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      var listing4 = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Hidden Draft Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.AddRange(listing1, listing2, listing3, listing4);

      db.ListingMedia.AddRange(
        new ListingMedia
        {
          Id = Guid.NewGuid(),
          ListingId = listing1.Id,
          Url = "https://example.com/rainbow.jpg",
          ContentLengthBytes = 1234,
          SortOrder = 0,
          IsPrimary = true,
          MediaType = ListingMediaTypes.Image,
          Status = ListingMediaStatuses.Ready,
          CreatedAt = now,
          UpdatedAt = now
        },
        new ListingMedia
        {
          Id = Guid.NewGuid(),
          ListingId = listing3.Id,
          Url = "https://example.com/vanadinite.jpg",
          ContentLengthBytes = 1234,
          SortOrder = 0,
          IsPrimary = true,
          MediaType = ListingMediaTypes.Image,
          Status = ListingMediaStatuses.Ready,
          CreatedAt = now,
          UpdatedAt = now
        });

      db.Auctions.AddRange(
        new Auction
        {
          Id = Guid.NewGuid(),
          ListingId = listing1.Id,
          Status = AuctionStatuses.Closing,
          StartingPriceCents = 10000,
          ReservePriceCents = null,
          StartTime = now.AddHours(-2),
          CloseTime = now.AddHours(4),
          ClosingWindowEnd = now.AddMinutes(20),
          CurrentPriceCents = 12500,
          CurrentLeaderUserId = null,
          CurrentLeaderMaxCents = null,
          BidCount = 3,
          ReserveMet = false,
          CreatedAt = now,
          UpdatedAt = now
        },
        new Auction
        {
          Id = Guid.NewGuid(),
          ListingId = listing2.Id,
          Status = AuctionStatuses.Live,
          StartingPriceCents = 20000,
          ReservePriceCents = null,
          StartTime = now.AddHours(-1),
          CloseTime = now.AddHours(2),
          ClosingWindowEnd = null,
          CurrentPriceCents = 20000,
          CurrentLeaderUserId = null,
          CurrentLeaderMaxCents = null,
          BidCount = 0,
          ReserveMet = false,
          CreatedAt = now,
          UpdatedAt = now
        },
        new Auction
        {
          Id = Guid.NewGuid(),
          ListingId = listing3.Id,
          Status = AuctionStatuses.Scheduled,
          StartingPriceCents = 18000,
          ReservePriceCents = null,
          StartTime = now.AddDays(1),
          CloseTime = now.AddDays(4),
          ClosingWindowEnd = null,
          CurrentPriceCents = 18000,
          CurrentLeaderUserId = null,
          CurrentLeaderMaxCents = null,
          BidCount = 0,
          ReserveMet = false,
          CreatedAt = now,
          UpdatedAt = now
        },
        new Auction
        {
          Id = Guid.NewGuid(),
          ListingId = listing4.Id,
          Status = AuctionStatuses.Draft,
          StartingPriceCents = 30000,
          ReservePriceCents = null,
          StartTime = now.AddHours(1),
          CloseTime = now.AddHours(6),
          ClosingWindowEnd = null,
          CurrentPriceCents = 30000,
          CurrentLeaderUserId = null,
          CurrentLeaderMaxCents = null,
          BidCount = 0,
          ReserveMet = false,
          CreatedAt = now,
          UpdatedAt = now
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var response = await client.GetAsync("/api/auctions");
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionBrowseResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().HaveCount(3);
    body.Total.Should().Be(3);

    body.Items.Should().Contain(x => x.Title == "Rainbow Fluorite Tower" && x.Status == AuctionStatuses.Closing);
    body.Items.Should().Contain(x => x.Title == "Amethyst Cathedral" && x.Status == AuctionStatuses.Live);
    body.Items.Should().Contain(x => x.Title == "Upcoming Vanadinite" && x.Status == AuctionStatuses.Scheduled);

    var closingItem = body.Items.Single(x => x.Title == "Rainbow Fluorite Tower");
    closingItem.PrimaryImageUrl.Should().Be("https://example.com/rainbow.jpg");
    closingItem.CurrentPriceCents.Should().Be(12500);
    closingItem.StartingPriceCents.Should().Be(10000);
    closingItem.BidCount.Should().Be(3);

    var scheduledItem = body.Items.Single(x => x.Title == "Upcoming Vanadinite");
    scheduledItem.PrimaryImageUrl.Should().Be("https://example.com/vanadinite.jpg");
    scheduledItem.CurrentPriceCents.Should().Be(18000);
    scheduledItem.StartingPriceCents.Should().Be(18000);
    scheduledItem.BidCount.Should().Be(0);
    scheduledItem.StartTimeUtc.Should().NotBeNull();
    scheduledItem.Status.Should().Be(AuctionStatuses.Scheduled);

    body.Items.Select(x => x.Status).Should().OnlyContain(x =>
      x == AuctionStatuses.Live ||
      x == AuctionStatuses.Closing ||
      x == AuctionStatuses.Scheduled);
  }

  [Fact]
  public async Task Get_auctions_returns_empty_list_when_no_public_auctions_exist()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);
    await ClearAuctionBrowseDataAsync(factory);

    using var client = factory.CreateClient();

    var response = await client.GetAsync("/api/auctions");
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionBrowseResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().BeEmpty();
    body.Total.Should().Be(0);
  }

  private TestAppFactory NewFactory()
    => new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task ClearAuctionBrowseDataAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    db.ListingMedia.RemoveRange(db.ListingMedia);
    db.Auctions.RemoveRange(db.Auctions);
    db.Listings.RemoveRange(db.Listings);

    await db.SaveChangesAsync();
  }
}