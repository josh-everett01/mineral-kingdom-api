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
  public async Task Get_auctions_returns_public_live_and_closing_auctions_sorted_by_closing_time()
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
        Title = "Hidden Draft Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.AddRange(listing1, listing2, listing3);

      db.ListingMedia.Add(new ListingMedia
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
          Status = "DRAFT",
          StartingPriceCents = 30000,
          ReservePriceCents = null,
          StartTime = now.AddHours(-1),
          CloseTime = now.AddHours(1),
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
    body!.Items.Should().HaveCount(2);
    body.Total.Should().Be(2);

    body.Items[0].Title.Should().Be("Rainbow Fluorite Tower");
    body.Items[0].PrimaryImageUrl.Should().Be("https://example.com/rainbow.jpg");
    body.Items[0].CurrentPriceCents.Should().Be(12500);
    body.Items[0].BidCount.Should().Be(3);
    body.Items[0].Status.Should().Be(AuctionStatuses.Closing);

    body.Items[1].Title.Should().Be("Amethyst Cathedral");
    body.Items[1].Status.Should().Be(AuctionStatuses.Live);

    body.Items.Select(x => x.Status).Should().OnlyContain(x =>
      x == AuctionStatuses.Live || x == AuctionStatuses.Closing);
  }

  [Fact]
  public async Task Get_auctions_returns_empty_list_when_no_public_auctions_exist()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

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
}