using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class OrderDetailTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public OrderDetailTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Owner_can_get_rich_order_detail()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();
    var orderId = Guid.NewGuid();
    var listingId = Guid.NewGuid();
    var mineralId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User
      {
        Id = ownerId,
        Email = $"owner-{ownerId:N}@example.com",
        PasswordHash = "x",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Minerals.Add(new Mineral
      {
        Id = mineralId,
        Name = "Fluorite",
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Purple Fluorite Cube",
        Description = "Display-ready fluorite specimen",
        Status = ListingStatuses.Published,
        PrimaryMineralId = mineralId,
        LocalityDisplay = "Denton Mine, Illinois, USA",
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.ListingMedia.Add(new ListingMedia
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        Url = "https://cdn.example.com/fluorite.jpg",
        ContentLengthBytes = 12345,
        SortOrder = 0,
        IsPrimary = true,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = ownerId,
        OrderNumber = "MK-20260328-ABC123",
        SourceType = "AUCTION",
        AuctionId = Guid.NewGuid(),
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(24),
        CurrencyCode = "USD",
        SubtotalCents = 5000,
        DiscountTotalCents = 500,
        TotalCents = 4500,
        CreatedAt = now,
        UpdatedAt = now,
        Lines =
        [
          new OrderLine
          {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OfferId = Guid.NewGuid(),
            ListingId = listingId,
            UnitPriceCents = 5000,
            UnitDiscountCents = 500,
            UnitFinalPriceCents = 4500,
            Quantity = 1,
            LineSubtotalCents = 5000,
            LineDiscountCents = 500,
            LineTotalCents = 4500,
            CreatedAt = now,
            UpdatedAt = now
          }
        ]
      });

      db.OrderPayments.Add(new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = "STRIPE",
        Status = "REDIRECTED",
        ProviderCheckoutId = "cs_test_123",
        AmountCents = 4500,
        CurrencyCode = "USD",
        CreatedAt = now.AddMinutes(1),
        UpdatedAt = now.AddMinutes(1)
      });

      db.OrderLedgerEntries.Add(new OrderLedgerEntry
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        EventType = "ORDER_PAYMENT_DUE_EXTENDED",
        DataJson = null,
        CreatedAt = now.AddMinutes(2)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, ownerId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.GetAsync($"/api/orders/{orderId}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<OrderDto>();
    dto.Should().NotBeNull();

    dto!.Id.Should().Be(orderId);
    dto.UserId.Should().Be(ownerId);
    dto.OrderNumber.Should().Be("MK-20260328-ABC123");
    dto.SourceType.Should().Be("AUCTION");
    dto.Status.Should().Be("AWAITING_PAYMENT");
    dto.PaymentStatus.Should().Be("REDIRECTED");
    dto.PaymentProvider.Should().Be("STRIPE");
    dto.TotalCents.Should().Be(4500);
    dto.CurrencyCode.Should().Be("USD");
    dto.PaymentDueAt.Should().NotBeNull();

    dto.Lines.Should().HaveCount(1);
    var line = dto.Lines.Single();
    line.ListingId.Should().Be(listingId);
    line.Title.Should().Be("Purple Fluorite Cube");
    line.ListingSlug.Should().Be("purple-fluorite-cube");
    line.PrimaryImageUrl.Should().Be("https://cdn.example.com/fluorite.jpg");
    line.MineralName.Should().Be("Fluorite");
    line.Locality.Should().Be("Denton Mine, Illinois, USA");
    line.Quantity.Should().Be(1);
    line.LineTotalCents.Should().Be(4500);

    dto.StatusHistory.Should().NotBeNull();
    dto.StatusHistory.Entries.Should().NotBeEmpty();
    dto.StatusHistory.Entries.Should().Contain(x => x.Type == "ORDER_CREATED");
    dto.StatusHistory.Entries.Should().Contain(x => x.Type == "PAYMENT_PENDING");
    dto.StatusHistory.Entries.Should().Contain(x => x.Type == "PAYMENT_REDIRECTED");
    dto.StatusHistory.Entries.Should().Contain(x => x.Type == "PAYMENT_DUE_EXTENDED");
  }

  [Fact]
  public async Task Non_owner_get_order_detail_is_forbidden()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var ownerId = Guid.NewGuid();
    var otherUserId = Guid.NewGuid();
    var orderId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.AddRange(
        new User
        {
          Id = ownerId,
          Email = $"owner-{ownerId:N}@example.com",
          PasswordHash = "x",
          EmailVerified = true,
          Role = UserRoles.User,
          CreatedAt = now.UtcDateTime,
          UpdatedAt = now.UtcDateTime
        },
        new User
        {
          Id = otherUserId,
          Email = $"other-{otherUserId:N}@example.com",
          PasswordHash = "x",
          EmailVerified = true,
          Role = UserRoles.User,
          CreatedAt = now.UtcDateTime,
          UpdatedAt = now.UtcDateTime
        });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = ownerId,
        OrderNumber = "MK-20260328-FORBID",
        SourceType = "STORE",
        Status = "DRAFT",
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, otherUserId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.GetAsync($"/api/orders/{orderId}");
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Missing_order_returns_not_found()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.User);

    var res = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}