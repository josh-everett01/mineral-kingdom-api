using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ListingDetailContextTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public ListingDetailContextTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_listing_detail_returns_enriched_public_fields_and_media()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var mineralName = $"Smoke Fluorite Detail {Guid.NewGuid():N}";

    Guid listingId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var mineral = new Mineral
      {
        Id = Guid.NewGuid(),
        Name = mineralName,
        CreatedAt = now,
        UpdatedAt = now
      };

      listingId = Guid.NewGuid();

      db.Minerals.Add(mineral);
      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Rainbow Fluorite Tower",
        Description = "A bright fluorescent fluorite specimen.",
        Status = MineralKingdom.Contracts.Listings.ListingStatuses.Published,
        PrimaryMineralId = mineral.Id,
        LocalityDisplay = "Berbes, Asturias, Spain",
        CountryCode = "ES",
        SizeClass = "CABINET",
        IsFluorescent = true,
        FluorescenceNotes = "Strong blue response under LWUV.",
        ConditionNotes = "Minor natural contact on reverse.",
        LengthCm = 8.5m,
        WidthCm = 4.2m,
        HeightCm = 3.8m,
        WeightGrams = 420,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-3),
        UpdatedAt = now.AddDays(-2),
        PublishedAt = now.AddDays(-2)
      });

      db.ListingMedia.AddRange(
        new ListingMedia
        {
          Id = Guid.NewGuid(),
          ListingId = listingId,
          MediaType = MineralKingdom.Contracts.Listings.ListingMediaTypes.Image,
          Status = MineralKingdom.Contracts.Listings.ListingMediaStatuses.Ready,
          Url = "https://media.example/fluorite-primary.jpg",
          SortOrder = 0,
          IsPrimary = true,
          Caption = "Primary image",
          ContentLengthBytes = 1234,
          CreatedAt = now,
          UpdatedAt = now
        },
        new ListingMedia
        {
          Id = Guid.NewGuid(),
          ListingId = listingId,
          MediaType = MineralKingdom.Contracts.Listings.ListingMediaTypes.Image,
          Status = MineralKingdom.Contracts.Listings.ListingMediaStatuses.Ready,
          Url = "https://media.example/fluorite-secondary.jpg",
          SortOrder = 1,
          IsPrimary = false,
          Caption = "Secondary image",
          ContentLengthBytes = 1234,
          CreatedAt = now,
          UpdatedAt = now
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var res = await client.GetAsync($"/api/listings/{listingId}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ListingDetailResponse>();
    dto.Should().NotBeNull();

    dto!.Title.Should().Be("Rainbow Fluorite Tower");
    dto.PrimaryMineral.Should().Be(mineralName);
    dto.LocalityDisplay.Should().Be("Berbes, Asturias, Spain");
    dto.SizeClass.Should().Be("CABINET");
    dto.IsFluorescent.Should().BeTrue();
    dto.FluorescenceNotes.Should().Be("Strong blue response under LWUV.");
    dto.ConditionNotes.Should().Be("Minor natural contact on reverse.");
    dto.Media.Should().HaveCount(2);
    dto.Media[0].IsPrimary.Should().BeTrue();
    dto.Media[0].Url.Should().Be("https://media.example/fluorite-primary.jpg");
  }

  [Fact]
  public async Task Get_listing_auction_returns_public_live_auction_snapshot()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid listingId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      listingId = Guid.NewGuid();

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Auction Quartz Cluster",
        Description = "Auction-backed specimen.",
        Status = MineralKingdom.Contracts.Listings.ListingStatuses.Published,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1)
      });

      db.Auctions.Add(new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 9500,
        CurrentPriceCents = 11200,
        ReservePriceCents = 14000,
        ReserveMet = false,
        BidCount = 4,
        StartTime = now.AddHours(-6),
        CloseTime = now.AddDays(2),
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var res = await client.GetAsync($"/api/listings/{listingId}/auction");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<AuctionRealtimeSnapshot>();
    dto.Should().NotBeNull();

    dto!.CurrentPriceCents.Should().Be(11200);
    dto.BidCount.Should().Be(4);
    dto.Status.Should().Be(AuctionStatuses.Live);
    dto.ReserveMet.Should().BeFalse();
    dto.MinimumNextBidCents.Should().BeGreaterThan(dto.CurrentPriceCents);
  }

  [Fact]
  public async Task Get_listing_auction_returns_not_found_when_no_public_auction_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid listingId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      listingId = Guid.NewGuid();

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Store-only specimen",
        Description = "Not in auction.",
        Status = MineralKingdom.Contracts.Listings.ListingStatuses.Published,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var res = await client.GetAsync($"/api/listings/{listingId}/auction");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private sealed record ListingMediaResponse(
    Guid Id,
    string MediaType,
    string Url,
    int SortOrder,
    bool IsPrimary,
    string? Caption);

  private sealed record ListingDetailResponse(
    Guid Id,
    string? Title,
    string? Description,
    string Status,
    Guid? PrimaryMineralId,
    string? PrimaryMineral,
    string? LocalityDisplay,
    string? CountryCode,
    string? SizeClass,
    bool IsFluorescent,
    string? FluorescenceNotes,
    string? ConditionNotes,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    int? WeightGrams,
    DateTimeOffset? PublishedAt,
    List<ListingMediaResponse> Media);
}