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

public sealed class AuctionDetailApiTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public AuctionDetailApiTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Get_auction_detail_returns_public_detail_payload()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Arkansas Quartz Cluster",
        Description = "A bright cluster with glassy points.",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.Add(listing);

      db.ListingMedia.AddRange(
        new ListingMedia
        {
          Id = Guid.NewGuid(),
          ListingId = listing.Id,
          Url = "https://example.com/quartz-primary.jpg",
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
          ListingId = listing.Id,
          Url = "https://example.com/quartz-secondary.jpg",
          ContentLengthBytes = 1234,
          SortOrder = 1,
          IsPrimary = false,
          MediaType = ListingMediaTypes.Image,
          Status = ListingMediaStatuses.Ready,
          CreatedAt = now,
          UpdatedAt = now
        });

      auctionId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(3),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = null,
        CurrentLeaderMaxCents = null,
        BidCount = 4,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var response = await client.GetAsync($"/api/auctions/{auctionId}/detail");
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.AuctionId.Should().Be(auctionId);
    body.Title.Should().Be("Arkansas Quartz Cluster");
    body.Description.Should().Be("A bright cluster with glassy points.");
    body.Status.Should().Be(AuctionStatuses.Live);
    body.CurrentPriceCents.Should().Be(11200);
    body.BidCount.Should().Be(4);
    body.Media.Should().HaveCount(2);
    body.Media[0].IsPrimary.Should().BeTrue();
    body.Media[0].Url.Should().Be("https://example.com/quartz-primary.jpg");
  }

  [Fact]
  public async Task Get_auction_detail_returns_not_found_when_missing()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    var response = await client.GetAsync($"/api/auctions/{Guid.NewGuid()}/detail");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Get_auction_detail_returns_member_aware_fields_null_for_anonymous_request()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var leaderUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Anonymous Detail Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.Add(listing);

      auctionId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = leaderUserId,
        CurrentLeaderMaxCents = 15000,
        BidCount = 4,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = leaderUserId,
        MaxBidCents = 15000,
        BidType = "IMMEDIATE",
        ReceivedAt = now,
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var response = await client.GetAsync($"/api/auctions/{auctionId}/detail");
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();
    body!.IsCurrentUserLeading.Should().BeNull();
    body.HasCurrentUserBid.Should().BeNull();
    body.CurrentUserMaxBidCents.Should().BeNull();
  }

  [Fact]
  public async Task Get_auction_detail_returns_leading_and_max_bid_for_authenticated_leader()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var leaderUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Winning Detail Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.Add(listing);

      auctionId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = leaderUserId,
        CurrentLeaderMaxCents = 15000,
        BidCount = 4,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = leaderUserId,
        MaxBidCents = 15000,
        BidType = "IMMEDIATE",
        ReceivedAt = now,
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", leaderUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();
    body!.HasCurrentUserBid.Should().BeTrue();
    body.IsCurrentUserLeading.Should().BeTrue();
    body.CurrentUserMaxBidCents.Should().Be(15000);
  }

  [Fact]
  public async Task Get_auction_detail_returns_outbid_state_and_current_max_bid_for_authenticated_non_leader_bidder()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var leaderUserId = Guid.NewGuid();
    var otherUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Outbid Detail Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.Add(listing);

      auctionId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = leaderUserId,
        CurrentLeaderMaxCents = 15000,
        BidCount = 4,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.AddRange(
        new AuctionMaxBid
        {
          AuctionId = auctionId,
          UserId = leaderUserId,
          MaxBidCents = 15000,
          BidType = "IMMEDIATE",
          ReceivedAt = now,
        },
        new AuctionMaxBid
        {
          AuctionId = auctionId,
          UserId = otherUserId,
          MaxBidCents = 14000,
          BidType = "IMMEDIATE",
          ReceivedAt = now,
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", otherUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();
    body!.HasCurrentUserBid.Should().BeTrue();
    body.IsCurrentUserLeading.Should().BeFalse();
    body.CurrentUserMaxBidCents.Should().Be(14000);
  }

  [Fact]
  public async Task Get_auction_detail_returns_no_participation_state_for_authenticated_user_with_no_bids()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var leaderUserId = Guid.NewGuid();
    var viewerUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "No Participation Detail Auction",
        Description = "Test",
        Status = ListingStatuses.Published,
        QuantityAvailable = 1,
        QuantityTotal = 1,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = now
      };

      db.Listings.Add(listing);

      auctionId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = leaderUserId,
        CurrentLeaderMaxCents = 15000,
        BidCount = 4,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = leaderUserId,
        MaxBidCents = 15000,
        BidType = "IMMEDIATE",
        ReceivedAt = now,
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", viewerUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();
    body!.HasCurrentUserBid.Should().BeFalse();
    body.IsCurrentUserLeading.Should().BeFalse();
    body.CurrentUserMaxBidCents.Should().BeNull();
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