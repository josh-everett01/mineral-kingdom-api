using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CartPageContractTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public CartPageContractTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Guest_cart_returns_enriched_line_summary_and_warning()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (offerId, listingId) = await SeedStoreListingAsync(factory);

    using var client = factory.CreateClient();

    var get1 = await client.GetAsync("/api/cart");
    get1.StatusCode.Should().Be(HttpStatusCode.OK);
    get1.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    var put = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(offerId, 5))
    };
    put.Headers.Add("X-Cart-Id", cartId);

    var putRes = await client.SendAsync(put);
    putRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await putRes.Content.ReadFromJsonAsync<CartDto>();
    dto.Should().NotBeNull();

    dto!.Warnings.Should().ContainSingle();
    dto.Warnings[0].Should().ContainEquivalentOf("not reserved");

    dto.Lines.Should().ContainSingle();
    dto.Lines[0].OfferId.Should().Be(offerId);
    dto.Lines[0].ListingId.Should().Be(listingId);
    dto.Lines[0].Title.Should().Be("Cart Fixture Fluorite");
    dto.Lines[0].ListingHref.Should().StartWith("/listing/cart-fixture-fluorite-");
    dto.Lines[0].PriceCents.Should().Be(18500);
    dto.Lines[0].EffectivePriceCents.Should().Be(16000);

    // 1-of-1 specimen rule: quantity should be fixed at 1
    dto.Lines[0].Quantity.Should().Be(1);
    dto.Lines[0].CanUpdateQuantity.Should().BeFalse();

    dto.SubtotalCents.Should().Be(16000);
  }

  [Fact]
  public async Task Guest_cart_remove_line_returns_empty_cart_and_same_cart_id()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (offerId, _) = await SeedStoreListingAsync(factory);

    using var client = factory.CreateClient();

    var get1 = await client.GetAsync("/api/cart");
    get1.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    var put = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(offerId, 1))
    };
    put.Headers.Add("X-Cart-Id", cartId);
    var putRes = await client.SendAsync(put);
    putRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/cart/lines/{offerId}");
    deleteReq.Headers.Add("X-Cart-Id", cartId);

    var deleteRes = await client.SendAsync(deleteReq);
    deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);
    deleteRes.Headers.TryGetValues("X-Cart-Id", out var deleteValues).Should().BeTrue();
    deleteValues!.Single().Should().Be(cartId);

    var dto = await deleteRes.Content.ReadFromJsonAsync<CartDto>();
    dto.Should().NotBeNull();
    dto!.Lines.Should().BeEmpty();
    dto.SubtotalCents.Should().Be(0);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<(Guid OfferId, Guid ListingId)> SeedStoreListingAsync(TestAppFactory factory)
  {
    var now = DateTimeOffset.UtcNow;

    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var mineral = new Mineral
    {
      Id = Guid.NewGuid(),
      Name = $"CartFixtureMineral-{Guid.NewGuid():N}",
      CreatedAt = now,
      UpdatedAt = now
    };

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Cart Fixture Fluorite",
      Description = "Fixture listing for cart contract tests.",
      Status = ListingStatuses.Published,
      PrimaryMineralId = mineral.Id,
      LocalityDisplay = "Berbes, Asturias, Spain",
      SizeClass = "CABINET",
      IsFluorescent = true,
      QuantityTotal = 1,
      QuantityAvailable = 1,
      CreatedAt = now.AddDays(-2),
      UpdatedAt = now.AddDays(-2),
      PublishedAt = now.AddDays(-2)
    };

    var media = new ListingMedia
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      MediaType = ListingMediaTypes.Image,
      Status = ListingMediaStatuses.Ready,
      Url = "https://media.example/cart-fixture.jpg",
      SortOrder = 0,
      IsPrimary = true,
      Caption = "Primary image",
      ContentLengthBytes = 1234,
      CreatedAt = now,
      UpdatedAt = now
    };

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      PriceCents = 18500,
      DiscountType = DiscountTypes.Flat,
      DiscountCents = 2500,
      IsActive = true,
      StartsAt = now.AddDays(-1),
      EndsAt = now.AddDays(30),
      CreatedAt = now.AddDays(-1),
      UpdatedAt = now.AddDays(-1)
    };

    db.Minerals.Add(mineral);
    db.Listings.Add(listing);
    db.ListingMedia.Add(media);
    db.StoreOffers.Add(offer);

    await db.SaveChangesAsync();

    return (offer.Id, listing.Id);
  }
}