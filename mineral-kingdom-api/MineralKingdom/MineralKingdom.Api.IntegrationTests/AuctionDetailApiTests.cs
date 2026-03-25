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

    body.IsCurrentUserLeading.Should().BeNull();
    body.HasCurrentUserBid.Should().BeNull();
    body.CurrentUserMaxBidCents.Should().BeNull();
    body.CurrentUserBidState.Should().BeNull();
    body.HasPendingDelayedBid.Should().BeNull();
    body.CurrentUserDelayedBidCents.Should().BeNull();
    body.CurrentUserDelayedBidStatus.Should().BeNull();
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
    body.CurrentUserBidState.Should().BeNull();
    body.HasPendingDelayedBid.Should().BeNull();
    body.CurrentUserDelayedBidCents.Should().BeNull();
    body.CurrentUserDelayedBidStatus.Should().BeNull();
  }

  [Fact]
  public async Task Get_auction_detail_returns_leading_and_no_delayed_state_for_authenticated_leader()
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
    body.CurrentUserBidState.Should().Be("LEADING");
    body.HasPendingDelayedBid.Should().BeFalse();
    body.CurrentUserDelayedBidCents.Should().BeNull();
    body.CurrentUserDelayedBidStatus.Should().Be("NONE");
  }

  [Fact]
  public async Task Get_auction_detail_returns_outbid_state_and_no_delayed_state_for_authenticated_non_leader_bidder()
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
    body.CurrentUserBidState.Should().Be("OUTBID");
    body.HasPendingDelayedBid.Should().BeFalse();
    body.CurrentUserDelayedBidCents.Should().BeNull();
    body.CurrentUserDelayedBidStatus.Should().Be("NONE");
  }

  [Fact]
  public async Task Get_auction_detail_returns_scheduled_delayed_bid_for_authenticated_delayed_only_user_before_closing()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var delayedUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Delayed Detail Auction",
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
        ReservePriceCents = 15000,
        StartTime = now.AddHours(-1),
        CloseTime = now.AddHours(8),
        ClosingWindowEnd = null,
        CurrentPriceCents = 10000,
        CurrentLeaderUserId = null,
        CurrentLeaderMaxCents = null,
        BidCount = 0,
        ReserveMet = false,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionDelayedBids.Add(new AuctionDelayedBid
      {
        AuctionId = auctionId,
        UserId = delayedUserId,
        MaxBidCents = 20000,
        Status = "SCHEDULED",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", delayedUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.HasCurrentUserBid.Should().BeTrue();
    body.IsCurrentUserLeading.Should().BeFalse();
    body.CurrentUserMaxBidCents.Should().BeNull();
    body.CurrentUserBidState.Should().Be("NONE");
    body.HasPendingDelayedBid.Should().BeTrue();
    body.CurrentUserDelayedBidCents.Should().Be(20000);
    body.CurrentUserDelayedBidStatus.Should().Be("SCHEDULED");
  }

  [Fact]
  public async Task Get_auction_detail_returns_moot_delayed_bid_when_current_price_exceeds_delayed_amount()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var delayedUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Moot By Price Auction",
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
        CloseTime = now.AddHours(8),
        ClosingWindowEnd = null,
        CurrentPriceCents = 25000,
        CurrentLeaderUserId = Guid.NewGuid(),
        CurrentLeaderMaxCents = 26000,
        BidCount = 5,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionDelayedBids.Add(new AuctionDelayedBid
      {
        AuctionId = auctionId,
        UserId = delayedUserId,
        MaxBidCents = 20000,
        Status = "SCHEDULED",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", delayedUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.CurrentUserBidState.Should().Be("NONE");
    body.HasPendingDelayedBid.Should().BeTrue();
    body.CurrentUserDelayedBidCents.Should().Be(20000);
    body.CurrentUserDelayedBidStatus.Should().Be("MOOT");
  }

  [Fact]
  public async Task Get_auction_detail_returns_moot_delayed_bid_when_immediate_bid_supersedes_delayed_amount()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var userId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Moot By Immediate Auction",
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
        CloseTime = now.AddHours(8),
        ClosingWindowEnd = null,
        CurrentPriceCents = 18000,
        CurrentLeaderUserId = userId,
        CurrentLeaderMaxCents = 25000,
        BidCount = 3,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = 25000,
        BidType = "IMMEDIATE",
        ReceivedAt = now
      });

      db.AuctionDelayedBids.Add(new AuctionDelayedBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = 20000,
        Status = "SCHEDULED",
        CreatedAt = now.AddMinutes(-10),
        UpdatedAt = now.AddMinutes(-10)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", userId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.CurrentUserBidState.Should().Be("LEADING");
    body.HasPendingDelayedBid.Should().BeTrue();
    body.CurrentUserDelayedBidCents.Should().Be(20000);
    body.CurrentUserDelayedBidStatus.Should().Be("MOOT");
  }

  [Fact]
  public async Task Get_auction_detail_returns_activated_delayed_bid_when_status_is_activated()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var userId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Activated Delayed Auction",
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
        Status = AuctionStatuses.Closing,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-3),
        CloseTime = now.AddMinutes(-5),
        ClosingWindowEnd = now.AddMinutes(5),
        CurrentPriceCents = 15000,
        CurrentLeaderUserId = userId,
        CurrentLeaderMaxCents = 20000,
        BidCount = 2,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.AuctionMaxBids.Add(new AuctionMaxBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = 20000,
        BidType = "IMMEDIATE",
        ReceivedAt = now
      });

      db.AuctionDelayedBids.Add(new AuctionDelayedBid
      {
        AuctionId = auctionId,
        UserId = userId,
        MaxBidCents = 20000,
        Status = "ACTIVATED",
        CreatedAt = now.AddHours(-1),
        UpdatedAt = now,
        ActivatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", userId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.CurrentUserBidState.Should().Be("LEADING");
    body.HasPendingDelayedBid.Should().BeTrue();
    body.CurrentUserDelayedBidCents.Should().Be(20000);
    body.CurrentUserDelayedBidStatus.Should().Be("ACTIVATED");
  }

  [Fact]
  public async Task Get_auction_detail_returns_none_delayed_state_when_user_has_no_delayed_bid()
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
        Title = "No Delayed Detail Auction",
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
        ReceivedAt = now
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
    body.CurrentUserBidState.Should().Be("NONE");
    body.HasPendingDelayedBid.Should().BeFalse();
    body.CurrentUserDelayedBidCents.Should().BeNull();
    body.CurrentUserDelayedBidStatus.Should().Be("NONE");
  }

  private TestAppFactory NewFactory()
    => new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  [Fact]
  public async Task Get_auction_detail_returns_payment_due_metadata_for_authenticated_winner()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    Guid orderId;
    var winnerUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Closed Auction Awaiting Payment",
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
      orderId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedWaitingOnPayment,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-5),
        CloseTime = now.AddHours(-1),
        ClosingWindowEnd = now.AddHours(-1),
        CurrentPriceCents = 18000,
        CurrentLeaderUserId = winnerUserId,
        CurrentLeaderMaxCents = 20000,
        BidCount = 3,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = winnerUserId,
        GuestEmail = null,
        OrderNumber = "MK-TEST-AUCTION-1",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        PaymentDueAt = now.AddDays(2),
        Status = "AWAITING_PAYMENT",
        CurrencyCode = "USD",
        SubtotalCents = 18000,
        DiscountTotalCents = 0,
        TotalCents = 18000,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", winnerUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.IsCurrentUserWinner.Should().BeTrue();
    body.PaymentOrderId.Should().Be(orderId);
    body.PaymentVisibilityState.Should().Be("PAYMENT_DUE");
  }

  [Fact]
  public async Task Get_auction_detail_returns_no_payment_visibility_for_authenticated_non_winner()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    var winnerUserId = Guid.NewGuid();
    var viewerUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Closed Auction Non Winner",
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
        Status = AuctionStatuses.ClosedWaitingOnPayment,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-5),
        CloseTime = now.AddHours(-1),
        ClosingWindowEnd = now.AddHours(-1),
        CurrentPriceCents = 18000,
        CurrentLeaderUserId = winnerUserId,
        CurrentLeaderMaxCents = 20000,
        BidCount = 3,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = Guid.NewGuid(),
        UserId = winnerUserId,
        GuestEmail = null,
        OrderNumber = "MK-TEST-AUCTION-2",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        PaymentDueAt = now.AddDays(2),
        Status = "AWAITING_PAYMENT",
        CurrencyCode = "USD",
        SubtotalCents = 18000,
        DiscountTotalCents = 0,
        TotalCents = 18000,
        CreatedAt = now,
        UpdatedAt = now
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

    body!.IsCurrentUserWinner.Should().BeFalse();
    body.PaymentOrderId.Should().BeNull();
    body.PaymentVisibilityState.Should().Be("NONE");
  }

  [Fact]
  public async Task Get_auction_detail_returns_paid_visibility_for_authenticated_winner_when_order_is_paid()
  {
    await using var factory = NewFactory();
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    Guid auctionId;
    Guid orderId;
    var winnerUserId = Guid.NewGuid();

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Closed Paid Auction",
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
      orderId = Guid.NewGuid();

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedPaid,
        StartingPriceCents = 10000,
        ReservePriceCents = null,
        StartTime = now.AddHours(-5),
        CloseTime = now.AddHours(-1),
        ClosingWindowEnd = now.AddHours(-1),
        CurrentPriceCents = 18000,
        CurrentLeaderUserId = winnerUserId,
        CurrentLeaderMaxCents = 20000,
        BidCount = 3,
        ReserveMet = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = winnerUserId,
        GuestEmail = null,
        OrderNumber = "MK-TEST-AUCTION-3",
        SourceType = "AUCTION",
        AuctionId = auctionId,
        PaymentDueAt = now.AddDays(-1),
        PaidAt = now.AddHours(-2),
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 18000,
        DiscountTotalCents = 0,
        TotalCents = 18000,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auctions/{auctionId}/detail");
    req.Headers.Add("X-Test-UserId", winnerUserId.ToString());

    var response = await client.SendAsync(req);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AuctionDetailDto>();
    body.Should().NotBeNull();

    body!.IsCurrentUserWinner.Should().BeTrue();
    body.PaymentOrderId.Should().Be(orderId);
    body.PaymentVisibilityState.Should().Be("PAID");
  }
}