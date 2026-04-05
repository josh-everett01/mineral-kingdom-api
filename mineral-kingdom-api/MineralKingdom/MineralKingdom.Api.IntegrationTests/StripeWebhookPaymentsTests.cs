using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class StripeWebhookPaymentsTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public StripeWebhookPaymentsTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Stripe_checkout_session_completed_webhook_completes_hold_and_sets_reference()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    await InsertStripeCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 1000, currency: "USD", providerCheckoutId: "cs_test_unit");

    var eventId = "evt_test_stripe_complete_1";
    var paymentIntentId = "pi_test_123";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentId, paymentIntentId);

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Stripe-Event-Id", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
      hold.CompletedAt.Should().NotBeNull();
      hold.PaymentReference.Should().Be(paymentIntentId);

      var pay = await db.CheckoutPayments.SingleAsync(p => p.Id == paymentId);
      pay.Status.Should().Be(CheckoutPaymentStatuses.Succeeded);
      pay.ProviderPaymentId.Should().Be(paymentIntentId);
    }
  }

  [Fact]
  public async Task Stripe_webhook_idempotency_same_event_twice_only_processes_once()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1500);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    await InsertStripeCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 1500, currency: "USD", providerCheckoutId: "cs_test_unit_2");

    var eventId = "evt_test_stripe_idempotency_1";
    var paymentIntentId = "pi_test_idem_1";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentId, paymentIntentId);

    async Task<HttpResponseMessage> PostOnceAsync()
    {
      var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
      {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
      };
      req.Headers.Add("X-Stripe-Event-Id", eventId);
      return await client.SendAsync(req);
    }

    var r1 = await PostOnceAsync();
    r1.StatusCode.Should().Be(HttpStatusCode.OK);

    var r2 = await PostOnceAsync();
    r2.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var count = await db.PaymentWebhookEvents
        .CountAsync(e => e.Provider == PaymentProviders.Stripe && e.EventId == eventId);

      count.Should().Be(1);
    }
  }

  [Fact]
  public async Task Stripe_webhook_completion_marks_listing_sold_disables_offer_and_deactivates_hold_items()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var offerId = await SeedOfferAsync(factory, priceCents: 1000);
    var client = factory.CreateClient();

    var cartId = await CreateGuestCartWithLineAsync(client, offerId);
    var start = await StartCheckoutAsync(client, cartId);

    var paymentId = Guid.NewGuid();
    await InsertStripeCheckoutPaymentAsync(factory, paymentId, start.HoldId, Guid.Parse(cartId), amountCents: 1000, currency: "USD", providerCheckoutId: "cs_test_unit_sold");

    var eventId = "evt_test_stripe_sold_1";
    var paymentIntentId = "pi_test_sold_1";
    var payload = StripeCheckoutSessionCompletedJson(start.HoldId, paymentId, paymentIntentId);

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Stripe-Event-Id", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Hold completes and reference set
      var hold = await db.CheckoutHolds.SingleAsync(h => h.Id == start.HoldId);
      hold.Status.Should().Be(CheckoutHoldStatuses.Completed);
      hold.PaymentReference.Should().Be(paymentIntentId);

      // Hold items -> source of truth for what got sold
      var holdItems = await db.CheckoutHoldItems
        .AsNoTracking()
        .Where(i => i.HoldId == start.HoldId)
        .Select(i => new { i.ListingId, i.OfferId, i.IsActive })
        .ToListAsync();

      holdItems.Should().NotBeEmpty();
      holdItems.All(x => x.IsActive == false).Should().BeTrue(); // deactivated

      var listingIds = holdItems.Select(x => x.ListingId).Distinct().ToList();
      var offerIds = holdItems.Select(x => x.OfferId).Distinct().ToList();

      // Listing(s) SOLD + qty 0
      var listings = await db.Listings.Where(l => listingIds.Contains(l.Id)).ToListAsync();
      listings.Should().NotBeEmpty();
      listings.All(l => l.Status == ListingStatuses.Sold).Should().BeTrue();
      listings.All(l => l.QuantityAvailable == 0).Should().BeTrue();

      // Offer(s) disabled
      var offers = await db.StoreOffers.Where(o => offerIds.Contains(o.Id) && o.DeletedAt == null).ToListAsync();
      offers.Should().NotBeEmpty();
      offers.All(o => o.IsActive == false).Should().BeTrue();
    }
  }

  // ---------------- helpers ----------------

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<string> SeedOfferAsync(TestAppFactory factory, int priceCents)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var listing = new Listing
    {
      Id = Guid.NewGuid(),
      Title = "Test Listing",
      Description = "Test",
      Status = ListingStatuses.Published,
      IsFluorescent = false,
      IsLot = false,
      QuantityTotal = 10,
      QuantityAvailable = 10,
      CreatedAt = now,
      UpdatedAt = now
    };
    db.Listings.Add(listing);

    var offer = new StoreOffer
    {
      Id = Guid.NewGuid(),
      ListingId = listing.Id,
      PriceCents = priceCents,
      DiscountType = DiscountTypes.None,
      IsActive = true,
      CreatedAt = now,
      UpdatedAt = now
    };
    db.StoreOffers.Add(offer);

    await db.SaveChangesAsync();
    return offer.Id.ToString();
  }

  private static async Task<string> CreateGuestCartWithLineAsync(HttpClient client, string offerId)
  {
    var get = await client.GetAsync("/api/cart");
    get.StatusCode.Should().Be(HttpStatusCode.OK);

    get.Headers.TryGetValues("X-Cart-Id", out var values).Should().BeTrue();
    var cartId = values!.Single();

    var put = new HttpRequestMessage(HttpMethod.Put, "/api/cart/lines")
    {
      Content = JsonContent.Create(new UpsertCartLineRequest(Guid.Parse(offerId), 1))
    };
    put.Headers.Add("X-Cart-Id", cartId);

    var putRes = await client.SendAsync(put);
    putRes.StatusCode.Should().Be(HttpStatusCode.OK);

    return cartId;
  }

  private static async Task<StartCheckoutResponse> StartCheckoutAsync(HttpClient client, string cartId)
  {
    const string GuestEmail = "guest@example.com";
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/start")
    {
      Content = JsonContent.Create(new StartCheckoutRequest(CartId: Guid.Parse(cartId), Email: GuestEmail))
    };
    req.Headers.Add("X-Cart-Id", cartId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<StartCheckoutResponse>();
    dto.Should().NotBeNull();
    return dto!;
  }

  private static async Task InsertStripeCheckoutPaymentAsync(
    TestAppFactory factory,
    Guid paymentId,
    Guid holdId,
    Guid cartId,
    int amountCents,
    string currency,
    string providerCheckoutId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var payment = new CheckoutPayment
    {
      Id = paymentId,
      HoldId = holdId,
      CartId = cartId,
      Provider = PaymentProviders.Stripe,
      Status = CheckoutPaymentStatuses.Redirected,
      AmountCents = amountCents,
      CurrencyCode = currency,
      ProviderCheckoutId = providerCheckoutId,
      CreatedAt = now,
      UpdatedAt = now
    };

    db.CheckoutPayments.Add(payment);
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Stripe_order_payment_webhook_confirms_open_box_order_and_assigns_open_box()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var userId = Guid.NewGuid();
    var orderId = Guid.NewGuid();
    var orderPaymentId = Guid.NewGuid();
    var auctionId = Guid.NewGuid();
    var listingId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "stripe_open_box_user@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Auction specimen",
        Description = "Auction listing",
        Status = ListingStatuses.Published,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Auctions.Add(new Auction
      {
        Id = auctionId,
        ListingId = listingId,
        Status = AuctionStatuses.ClosedWaitingOnPayment,
        CurrentLeaderUserId = userId,
        CurrentPriceCents = 10500,
        StartingPriceCents = 1000,
        CloseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-10), DateTimeKind.Utc),
        StartTime = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Utc),
        BidCount = 3,
        ReserveMet = true,
        CreatedAt = now.AddHours(-2),
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        GuestEmail = null,
        OrderNumber = $"MK-OBX-{Guid.NewGuid():N}"[..18],
        SourceType = "AUCTION",
        AuctionId = auctionId,
        Status = "AWAITING_PAYMENT",
        ShippingMode = "OPEN_BOX",
        PaymentDueAt = now.AddDays(2),
        PaidAt = null,
        CurrencyCode = "USD",
        SubtotalCents = 10500,
        DiscountTotalCents = 0,
        TotalCents = 10500,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.OrderPayments.Add(new OrderPayment
      {
        Id = orderPaymentId,
        OrderId = orderId,
        Provider = PaymentProviders.Stripe,
        Status = CheckoutPaymentStatuses.Redirected,
        ProviderCheckoutId = "cs_test_order_open_box_1",
        ProviderPaymentId = null,
        AmountCents = 10500,
        CurrencyCode = "USD",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();

    var eventId = "evt_test_stripe_order_open_box_1";
    var paymentIntentId = "pi_test_order_open_box_1";
    var payload = $$"""
{
  "id": "{{eventId}}",
  "type": "checkout.session.completed",
  "data": {
    "object": {
      "object": "checkout.session",
      "id": "cs_test_order_open_box_1",
      "payment_status": "paid",
      "payment_intent": "{{paymentIntentId}}",
      "metadata": {
        "order_id": "{{orderId}}",
        "order_payment_id": "{{orderPaymentId}}"
      }
    }
  }
}
""";

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Stripe-Event-Id", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var orderPayment = await db.OrderPayments.SingleAsync(x => x.Id == orderPaymentId);
      orderPayment.Status.Should().Be(CheckoutPaymentStatuses.Succeeded);
      orderPayment.ProviderPaymentId.Should().Be(paymentIntentId);

      var order = await db.Orders.SingleAsync(x => x.Id == orderId);
      order.Status.Should().Be("READY_TO_FULFILL");
      order.PaidAt.Should().NotBeNull();
      order.FulfillmentGroupId.Should().NotBeNull();

      var box = await db.FulfillmentGroups.SingleAsync(x => x.Id == order.FulfillmentGroupId!.Value);
      box.UserId.Should().Be(userId);
      box.BoxStatus.Should().Be("OPEN");

      var auction = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auction.Status.Should().Be(AuctionStatuses.ClosedPaid);

      var listing = await db.Listings.SingleAsync(x => x.Id == listingId);
      listing.Status.Should().Be(ListingStatuses.Sold);
      listing.QuantityAvailable.Should().Be(0);

      var webhookEvent = await db.PaymentWebhookEvents.SingleAsync(
        x => x.Provider == PaymentProviders.Stripe && x.EventId == eventId);

      webhookEvent.OrderPaymentId.Should().Be(orderPaymentId);
    }
  }

  private static string StripeCheckoutSessionCompletedJson(Guid holdId, Guid paymentId, string paymentIntentId)
  {
    return $$"""
    {
      "id": "evt_placeholder",
      "type": "checkout.session.completed",
      "data": {
        "object": {
          "object": "checkout.session",
          "payment_status": "READY_TO_FULFILL",
          "payment_intent": "{{paymentIntentId}}",
          "metadata": {
            "hold_id": "{{holdId}}",
            "payment_id": "{{paymentId}}"
          }
        }
      }
    }
    """;
  }

  [Fact]
  public async Task Stripe_checkout_session_completed_webhook_marks_shipping_invoice_paid()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var userId = Guid.NewGuid();
    var groupId = Guid.NewGuid();
    var invoiceId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "stripe_ship_webhook@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = userId,
        GuestEmail = null,
        BoxStatus = "CLOSED",
        ClosedAt = now,
        Status = "READY_TO_FULFILL",
        CreatedAt = now,
        UpdatedAt = now
      });

      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = invoiceId,
        FulfillmentGroupId = groupId,
        AmountCents = 599,
        CalculatedAmountCents = 599,
        CurrencyCode = "USD",
        Status = "UNPAID",
        Provider = PaymentProviders.Stripe,
        ProviderCheckoutId = "cs_test_ship_webhook_1",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    var client = factory.CreateClient();

    var eventId = "evt_test_stripe_ship_invoice_1";
    var paymentIntentId = "pi_test_ship_123";
    var payload = $$"""
{
  "id": "evt_placeholder",
  "type": "checkout.session.completed",
  "data": {
    "object": {
      "object": "checkout.session",
      "id": "cs_test_ship_webhook_1",
      "payment_intent": "{{paymentIntentId}}",
      "metadata": {
        "shipping_invoice_id": "{{invoiceId}}"
      }
    }
  }
}
""";

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Stripe-Event-Id", eventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var invoice = await db.ShippingInvoices.SingleAsync(i => i.Id == invoiceId);
      invoice.Status.Should().Be("PAID");
      invoice.PaidAt.Should().NotBeNull();
      invoice.ProviderPaymentId.Should().Be(paymentIntentId);

      var webhookEvent = await db.PaymentWebhookEvents
        .SingleAsync(e => e.Provider == PaymentProviders.Stripe && e.EventId == eventId);

      webhookEvent.ProcessedAt.Should().NotBeNull();
    }
  }
}
