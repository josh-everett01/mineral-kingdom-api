using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/testing/e2e")]
[AllowAnonymous]
public sealed class TestingE2ESeedController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  private readonly IWebHostEnvironment _env;

  public TestingE2ESeedController(
    MineralKingdomDbContext db,
    IWebHostEnvironment env)
  {
    _db = db;
    _env = env;
  }

  [HttpPost("seed")]
  public async Task<ActionResult<E2ESeedResponse>> Seed(CancellationToken ct)
  {
    if (!_env.IsEnvironment("Testing"))
      return NotFound();

    var now = DateTimeOffset.UtcNow;

    var fluoriteMineralId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var quartzMineralId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    var storeListingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    var storeMediaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    var storeOfferId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");

    var storeListing2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4");
    var storeMedia2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5");
    var storeOffer2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6");

    var storeListing3Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7");
    var storeMedia3Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa8");
    var storeOffer3Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa9");

    var auctionListingId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    var auctionMediaId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    var auctionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3");

    var adminAuctionDraftListingId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    var adminAuctionDraftMediaId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2");
    const string adminAuctionDraftListingTitle = "Admin Auction Draft Fixture";

    var adminAuctionScheduledListingId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");
    var adminAuctionScheduledMediaId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd2");
    const string adminAuctionScheduledListingTitle = "Admin Auction Scheduled Fixture";

    var adminAuctionLiveListingId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1");
    var adminAuctionLiveMediaId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee2");
    const string adminAuctionLiveListingTitle = "Admin Auction Live Fixture";

    var adminOrderId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    const string adminOrderNumber = "MK-E2E-ADMIN-ORDER-1";
    const string adminOrderEmail = "smoke_user1@example.com";
    var adminOrderPaymentId = Guid.Parse("f1111111-1111-1111-1111-111111111112");

    await UpsertMineralAsync(
      fluoriteMineralId,
      "Smoke Fluorite E2E",
      now,
      ct);

    await UpsertMineralAsync(
      quartzMineralId,
      "Smoke Quartz E2E",
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = storeListingId,
        Title = "Rainbow Fluorite Tower",
        Description = "Deterministic E2E store listing fixture.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = fluoriteMineralId,
        LocalityDisplay = "Berbes, Asturias, Spain",
        CountryCode = "ES",
        AdminArea1 = "Asturias",
        MineName = "Berbes District",
        LengthCm = 8.5m,
        WidthCm = 4.2m,
        HeightCm = 3.8m,
        WeightGrams = 420,
        SizeClass = "CABINET",
        IsFluorescent = true,
        FluorescenceNotes = "Strong blue fluorescence under LWUV.",
        ConditionNotes = "Excellent edges with minor natural contact on reverse.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        PublishedAt = now.AddDays(-2),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = storeMediaId,
        ListingId = storeListingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "rainbow-fluorite-tower.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 204800,
        Url = "https://images.unsplash.com/photo-1518066000714-58c45f1a2c0a?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Rainbow Fluorite Tower primary image",
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        DeletedAt = null
      },
      ct);

    await UpsertStoreOfferAsync(
      new StoreOffer
      {
        Id = storeOfferId,
        ListingId = storeListingId,
        PriceCents = 18500,
        DiscountType = DiscountTypes.Flat,
        DiscountCents = 2500,
        DiscountPercentBps = null,
        IsActive = true,
        StartsAt = now.AddDays(-1),
        EndsAt = now.AddDays(30),
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetCheckoutStateAsync(
      storeListingId,
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = storeListing2Id,
        Title = "Amethyst Cathedral",
        Description = "Deterministic E2E store listing fixture B.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = quartzMineralId,
        LocalityDisplay = "Artigas, Uruguay",
        CountryCode = "UY",
        AdminArea1 = "Artigas",
        MineName = "Artigas District",
        LengthCm = 11.2m,
        WidthCm = 6.4m,
        HeightCm = 5.9m,
        WeightGrams = 780,
        SizeClass = "CABINET",
        IsFluorescent = false,
        FluorescenceNotes = null,
        ConditionNotes = "Rich purple zoning with polished base.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        PublishedAt = now.AddDays(-2),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = storeMedia2Id,
        ListingId = storeListing2Id,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "amethyst-cathedral.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 198400,
        Url = "https://images.unsplash.com/photo-1510017803434-a899398421b3?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Amethyst Cathedral primary image",
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        DeletedAt = null
      },
      ct);

    await UpsertStoreOfferAsync(
      new StoreOffer
      {
        Id = storeOffer2Id,
        ListingId = storeListing2Id,
        PriceCents = 24900,
        DiscountType = DiscountTypes.None,
        DiscountCents = null,
        DiscountPercentBps = null,
        IsActive = true,
        StartsAt = now.AddDays(-1),
        EndsAt = now.AddDays(30),
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetCheckoutStateAsync(
      storeListing2Id,
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = storeListing3Id,
        Title = "Celestite Geode",
        Description = "Deterministic E2E store listing fixture C.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = quartzMineralId,
        LocalityDisplay = "Sakoany, Madagascar",
        CountryCode = "MG",
        AdminArea1 = "Boeny",
        MineName = "Sakoany District",
        LengthCm = 9.4m,
        WidthCm = 6.1m,
        HeightCm = 5.0m,
        WeightGrams = 610,
        SizeClass = "CABINET",
        IsFluorescent = false,
        FluorescenceNotes = null,
        ConditionNotes = "Sky-blue crystals with natural rim.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        PublishedAt = now.AddDays(-2),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = storeMedia3Id,
        ListingId = storeListing3Id,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "celestite-geode.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 201000,
        Url = "https://images.unsplash.com/photo-1511497584788-876760111969?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Celestite Geode primary image",
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2),
        DeletedAt = null
      },
      ct);

    await UpsertStoreOfferAsync(
      new StoreOffer
      {
        Id = storeOffer3Id,
        ListingId = storeListing3Id,
        PriceCents = 21900,
        DiscountType = DiscountTypes.None,
        DiscountCents = null,
        DiscountPercentBps = null,
        IsActive = true,
        StartsAt = now.AddDays(-1),
        EndsAt = now.AddDays(30),
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetCheckoutStateAsync(
      storeListing3Id,
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = auctionListingId,
        Title = "Arkansas Quartz Cluster",
        Description = "Deterministic E2E auction listing fixture.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = quartzMineralId,
        LocalityDisplay = "Mount Ida, Arkansas, USA",
        CountryCode = "US",
        AdminArea1 = "Arkansas",
        MineName = "Mount Ida district",
        LengthCm = 12.0m,
        WidthCm = 8.0m,
        HeightCm = 6.0m,
        WeightGrams = 860,
        SizeClass = "MINIATURE",
        IsFluorescent = false,
        FluorescenceNotes = null,
        ConditionNotes = "Bright luster with minor edge contacts.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = auctionMediaId,
        ListingId = auctionListingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "arkansas-quartz-cluster.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 225000,
        Url = "https://images.unsplash.com/photo-1523712999610-f77fbcfc3843?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Arkansas Quartz Cluster primary image",
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetAuctionStateAsync(
      auctionListingId,
      auctionId,
      now,
      ct);

    await UpsertAuctionAsync(
      new Auction
      {
        Id = auctionId,
        ListingId = auctionListingId,
        Status = AuctionStatuses.Live,
        StartingPriceCents = 9500,
        ReservePriceCents = 14000,
        StartTime = now.AddHours(-6),
        CloseTime = now.AddDays(2),
        ClosingWindowEnd = null,
        CurrentPriceCents = 11200,
        CurrentLeaderUserId = null,
        CurrentLeaderMaxCents = null,
        BidCount = 4,
        ReserveMet = false,
        RelistOfAuctionId = null,
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1)
      },
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = adminAuctionDraftListingId,
        Title = adminAuctionDraftListingTitle,
        Description = "Deterministic E2E admin auction draft fixture.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = fluoriteMineralId,
        LocalityDisplay = "Naica, Chihuahua, Mexico",
        CountryCode = "MX",
        AdminArea1 = "Chihuahua",
        MineName = "Naica Mine",
        LengthCm = 7.5m,
        WidthCm = 4.0m,
        HeightCm = 3.0m,
        WeightGrams = 390,
        SizeClass = "SMALL CABINET",
        IsFluorescent = false,
        FluorescenceNotes = null,
        ConditionNotes = "Ready for admin auction draft testing.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = adminAuctionDraftMediaId,
        ListingId = adminAuctionDraftListingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "admin-auction-draft-fixture.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 180000,
        Url = "https://images.unsplash.com/photo-1501004318641-b39e6451bec6?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Admin auction draft fixture primary image",
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetAdminAuctionCreationStateAsync(
      adminAuctionDraftListingId,
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = adminAuctionScheduledListingId,
        Title = adminAuctionScheduledListingTitle,
        Description = "Deterministic E2E admin auction scheduled fixture.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = quartzMineralId,
        LocalityDisplay = "Tsumeb, Namibia",
        CountryCode = "NA",
        AdminArea1 = "Otjikoto",
        MineName = "Tsumeb Mine",
        LengthCm = 8.2m,
        WidthCm = 5.3m,
        HeightCm = 4.1m,
        WeightGrams = 470,
        SizeClass = "CABINET",
        IsFluorescent = false,
        FluorescenceNotes = null,
        ConditionNotes = "Ready for admin auction scheduled testing.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = adminAuctionScheduledMediaId,
        ListingId = adminAuctionScheduledListingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "admin-auction-scheduled-fixture.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 182000,
        Url = "https://images.unsplash.com/photo-1511497584788-876760111969?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Admin auction scheduled fixture primary image",
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetAdminAuctionCreationStateAsync(
      adminAuctionScheduledListingId,
      now,
      ct);

    await UpsertListingAsync(
      new Listing
      {
        Id = adminAuctionLiveListingId,
        Title = adminAuctionLiveListingTitle,
        Description = "Deterministic E2E admin auction live fixture.",
        Status = ListingStatuses.Published,
        PrimaryMineralId = fluoriteMineralId,
        LocalityDisplay = "Weardale, County Durham, England",
        CountryCode = "GB",
        AdminArea1 = "County Durham",
        MineName = "Rogerley Mine",
        LengthCm = 7.9m,
        WidthCm = 4.6m,
        HeightCm = 3.7m,
        WeightGrams = 410,
        SizeClass = "SMALL CABINET",
        IsFluorescent = true,
        FluorescenceNotes = "Green fluorescence under LWUV.",
        ConditionNotes = "Ready for admin auction launch-now testing.",
        IsLot = false,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        PublishedAt = now.AddDays(-1),
        ArchivedAt = null
      },
      ct);

    await UpsertListingMediaAsync(
      new ListingMedia
      {
        Id = adminAuctionLiveMediaId,
        ListingId = adminAuctionLiveListingId,
        MediaType = ListingMediaTypes.Image,
        Status = ListingMediaStatuses.Ready,
        StorageKey = null,
        OriginalFileName = "admin-auction-live-fixture.jpg",
        ContentType = "image/jpeg",
        ContentLengthBytes = 184000,
        Url = "https://images.unsplash.com/photo-1518066000714-58c45f1a2c0a?auto=format&fit=crop&w=1200&q=80",
        SortOrder = 0,
        IsPrimary = true,
        Caption = "Admin auction live fixture primary image",
        CreatedAt = now.AddDays(-1),
        UpdatedAt = now.AddDays(-1),
        DeletedAt = null
      },
      ct);

    await ResetAdminAuctionCreationStateAsync(
      adminAuctionLiveListingId,
      now,
      ct);

    await UpsertOrderAsync(
      new Order
      {
        Id = adminOrderId,
        UserId = null,
        GuestEmail = adminOrderEmail,
        OrderNumber = adminOrderNumber,
        SourceType = "STORE",
        AuctionId = null,
        CheckoutHoldId = null,
        PaymentDueAt = null,
        PaidAt = now.AddHours(-6),
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 24900,
        DiscountTotalCents = 0,
        ShippingAmountCents = 0,
        ShippingMode = "SHIP_NOW",
        TotalCents = 24900,
        CreatedAt = now.AddHours(-6),
        UpdatedAt = now.AddHours(-6)
      },
      ct);

    await UpsertOrderPaymentAsync(
      new OrderPayment
      {
        Id = adminOrderPaymentId,
        OrderId = adminOrderId,
        Provider = "STRIPE",
        Status = CheckoutPaymentStatuses.Succeeded,
        AmountCents = 24900,
        CurrencyCode = "USD",
        ProviderCheckoutId = "cs_test_e2e_admin_order_1",
        ProviderPaymentId = "pi_test_e2e_admin_order_1",
        CreatedAt = now.AddHours(-6),
        UpdatedAt = now.AddHours(-6)
      },
      ct);

    return Ok(new E2ESeedResponse(
      StoreListingId: storeListingId,
      StoreOfferId: storeOfferId,
      StoreListing2Id: storeListing2Id,
      StoreOffer2Id: storeOffer2Id,
      StoreListing3Id: storeListing3Id,
      StoreOffer3Id: storeOffer3Id,
      AuctionListingId: auctionListingId,
      AuctionId: auctionId,
      AdminAuctionDraftListingId: adminAuctionDraftListingId,
      AdminAuctionDraftListingTitle: adminAuctionDraftListingTitle,
      AdminAuctionScheduledListingId: adminAuctionScheduledListingId,
      AdminAuctionScheduledListingTitle: adminAuctionScheduledListingTitle,
      AdminAuctionLiveListingId: adminAuctionLiveListingId,
      AdminAuctionLiveListingTitle: adminAuctionLiveListingTitle,
      AdminOrderId: adminOrderId,
      AdminOrderNumber: adminOrderNumber,
      AdminOrderEmail: adminOrderEmail
    ));
  }

  private async Task UpsertMineralAsync(Guid id, string name, DateTimeOffset now, CancellationToken ct)
  {
    var existing = await _db.Minerals.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (existing is null)
    {
      _db.Minerals.Add(new Mineral
      {
        Id = id,
        Name = name,
        CreatedAt = now,
        UpdatedAt = now
      });
    }
    else
    {
      existing.Name = name;
      existing.UpdatedAt = now;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertListingAsync(Listing seed, CancellationToken ct)
  {
    var existing = await _db.Listings.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.Listings.Add(seed);
    }
    else
    {
      existing.Title = seed.Title;
      existing.Description = seed.Description;
      existing.Status = seed.Status;
      existing.PrimaryMineralId = seed.PrimaryMineralId;
      existing.LocalityDisplay = seed.LocalityDisplay;
      existing.CountryCode = seed.CountryCode;
      existing.AdminArea1 = seed.AdminArea1;
      existing.MineName = seed.MineName;
      existing.LengthCm = seed.LengthCm;
      existing.WidthCm = seed.WidthCm;
      existing.HeightCm = seed.HeightCm;
      existing.WeightGrams = seed.WeightGrams;
      existing.SizeClass = seed.SizeClass;
      existing.IsFluorescent = seed.IsFluorescent;
      existing.FluorescenceNotes = seed.FluorescenceNotes;
      existing.ConditionNotes = seed.ConditionNotes;
      existing.IsLot = seed.IsLot;
      existing.QuantityTotal = seed.QuantityTotal;
      existing.QuantityAvailable = seed.QuantityAvailable;
      existing.PublishedAt = seed.PublishedAt;
      existing.ArchivedAt = seed.ArchivedAt;
      existing.UpdatedAt = seed.UpdatedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertListingMediaAsync(ListingMedia seed, CancellationToken ct)
  {
    var existing = await _db.ListingMedia.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.ListingMedia.Add(seed);
    }
    else
    {
      existing.ListingId = seed.ListingId;
      existing.MediaType = seed.MediaType;
      existing.Status = seed.Status;
      existing.StorageKey = seed.StorageKey;
      existing.OriginalFileName = seed.OriginalFileName;
      existing.ContentType = seed.ContentType;
      existing.ContentLengthBytes = seed.ContentLengthBytes;
      existing.Url = seed.Url;
      existing.SortOrder = seed.SortOrder;
      existing.IsPrimary = seed.IsPrimary;
      existing.Caption = seed.Caption;
      existing.UpdatedAt = seed.UpdatedAt;
      existing.DeletedAt = seed.DeletedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertStoreOfferAsync(StoreOffer seed, CancellationToken ct)
  {
    var existing = await _db.StoreOffers.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.StoreOffers.Add(seed);
    }
    else
    {
      existing.ListingId = seed.ListingId;
      existing.PriceCents = seed.PriceCents;
      existing.DiscountType = seed.DiscountType;
      existing.DiscountCents = seed.DiscountCents;
      existing.DiscountPercentBps = seed.DiscountPercentBps;
      existing.IsActive = seed.IsActive;
      existing.StartsAt = seed.StartsAt;
      existing.EndsAt = seed.EndsAt;
      existing.UpdatedAt = seed.UpdatedAt;
      existing.DeletedAt = seed.DeletedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertAuctionAsync(Auction seed, CancellationToken ct)
  {
    var existing = await _db.Auctions.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.Auctions.Add(seed);
    }
    else
    {
      existing.ListingId = seed.ListingId;
      existing.Status = seed.Status;
      existing.StartingPriceCents = seed.StartingPriceCents;
      existing.ReservePriceCents = seed.ReservePriceCents;
      existing.StartTime = seed.StartTime;
      existing.CloseTime = seed.CloseTime;
      existing.ClosingWindowEnd = seed.ClosingWindowEnd;
      existing.CurrentPriceCents = seed.CurrentPriceCents;
      existing.CurrentLeaderUserId = seed.CurrentLeaderUserId;
      existing.CurrentLeaderMaxCents = seed.CurrentLeaderMaxCents;
      existing.BidCount = seed.BidCount;
      existing.ReserveMet = seed.ReserveMet;
      existing.RelistOfAuctionId = seed.RelistOfAuctionId;
      existing.UpdatedAt = seed.UpdatedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertOrderAsync(Order seed, CancellationToken ct)
  {
    var existing = await _db.Orders.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.Orders.Add(seed);
    }
    else
    {
      existing.UserId = seed.UserId;
      existing.GuestEmail = seed.GuestEmail;
      existing.OrderNumber = seed.OrderNumber;
      existing.SourceType = seed.SourceType;
      existing.AuctionId = seed.AuctionId;
      existing.CheckoutHoldId = seed.CheckoutHoldId;
      existing.PaymentDueAt = seed.PaymentDueAt;
      existing.PaidAt = seed.PaidAt;
      existing.Status = seed.Status;
      existing.CurrencyCode = seed.CurrencyCode;
      existing.SubtotalCents = seed.SubtotalCents;
      existing.DiscountTotalCents = seed.DiscountTotalCents;
      existing.ShippingAmountCents = seed.ShippingAmountCents;
      existing.ShippingMode = seed.ShippingMode;
      existing.TotalCents = seed.TotalCents;
      existing.UpdatedAt = seed.UpdatedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task UpsertOrderPaymentAsync(OrderPayment seed, CancellationToken ct)
  {
    var existing = await _db.OrderPayments.SingleOrDefaultAsync(x => x.Id == seed.Id, ct);
    if (existing is null)
    {
      _db.OrderPayments.Add(seed);
    }
    else
    {
      existing.OrderId = seed.OrderId;
      existing.Provider = seed.Provider;
      existing.Status = seed.Status;
      existing.AmountCents = seed.AmountCents;
      existing.CurrencyCode = seed.CurrencyCode;
      existing.ProviderCheckoutId = seed.ProviderCheckoutId;
      existing.ProviderPaymentId = seed.ProviderPaymentId;
      existing.UpdatedAt = seed.UpdatedAt;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task ResetCheckoutStateAsync(
    Guid listingId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var activeHoldItems = await _db.CheckoutHoldItems
      .Where(x => x.ListingId == listingId && x.IsActive)
      .ToListAsync(ct);

    if (activeHoldItems.Count == 0)
      return;

    var holdIds = activeHoldItems
      .Select(x => x.HoldId)
      .Distinct()
      .ToList();

    foreach (var item in activeHoldItems)
    {
      item.IsActive = false;
    }

    var holds = await _db.CheckoutHolds
      .Where(x => holdIds.Contains(x.Id))
      .ToListAsync(ct);

    foreach (var hold in holds)
    {
      if (hold.Status == CheckoutHoldStatuses.Active)
      {
        hold.Status = CheckoutHoldStatuses.Expired;
        hold.UpdatedAt = now;
        if (hold.ExpiresAt > now)
        {
          hold.ExpiresAt = now;
        }
      }
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task ResetAuctionStateAsync(
    Guid listingId,
    Guid seededAuctionId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var otherLiveOrClosingAuctions = await _db.Auctions
      .Where(x =>
        x.ListingId == listingId &&
        x.Id != seededAuctionId &&
        (x.Status == AuctionStatuses.Live || x.Status == AuctionStatuses.Closing))
      .ToListAsync(ct);

    foreach (var auction in otherLiveOrClosingAuctions)
    {
      auction.Status = AuctionStatuses.ClosedNotSold;
      auction.UpdatedAt = now;
      auction.CloseTime = now;
      auction.ClosingWindowEnd = now;
      auction.CurrentLeaderUserId = null;
      auction.CurrentLeaderMaxCents = null;
      auction.BidCount = 0;
      auction.CurrentPriceCents = auction.StartingPriceCents;
      auction.ReserveMet = false;
    }

    await _db.SaveChangesAsync(ct);
  }

  private async Task ResetAdminAuctionCreationStateAsync(
    Guid listingId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var auctions = await _db.Auctions
      .Where(x =>
        x.ListingId == listingId &&
        (x.Status == AuctionStatuses.Draft ||
         x.Status == AuctionStatuses.Scheduled ||
         x.Status == AuctionStatuses.Live ||
         x.Status == AuctionStatuses.Closing ||
         x.Status == AuctionStatuses.ClosedWaitingOnPayment))
      .ToListAsync(ct);

    foreach (var auction in auctions)
    {
      auction.Status = AuctionStatuses.ClosedNotSold;
      auction.UpdatedAt = now;
      auction.CloseTime = now;
      auction.ClosingWindowEnd = now;
      auction.CurrentLeaderUserId = null;
      auction.CurrentLeaderMaxCents = null;
      auction.BidCount = 0;
      auction.CurrentPriceCents = auction.StartingPriceCents;
      auction.ReserveMet = false;
    }

    await _db.SaveChangesAsync(ct);
  }

  public sealed record E2ESeedResponse(
    Guid StoreListingId,
    Guid StoreOfferId,
    Guid StoreListing2Id,
    Guid StoreOffer2Id,
    Guid StoreListing3Id,
    Guid StoreOffer3Id,
    Guid AuctionListingId,
    Guid AuctionId,
    Guid AdminAuctionDraftListingId,
    string AdminAuctionDraftListingTitle,
    Guid AdminAuctionScheduledListingId,
    string AdminAuctionScheduledListingTitle,
    Guid AdminAuctionLiveListingId,
    string AdminAuctionLiveListingTitle,
    Guid AdminOrderId,
    string AdminOrderNumber,
    string AdminOrderEmail
  );
}