using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class AuctionOrderInventoryWebhookTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public AuctionOrderInventoryWebhookTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Stripe_webhook_for_auction_order_marks_listing_sold_drains_inventory_and_disables_offers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var utc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    Guid listingId;
    Guid offerId;
    Guid auctionId;
    Guid orderId;
    Guid orderPaymentId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      // Listing + offer
      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "Stripe Auction Listing",
        Status = ListingStatuses.Published,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);

      var offer = new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        PriceCents = 1234,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.StoreOffers.Add(offer);

      // Auction that references listing
      var auction = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedWaitingOnPayment,
        CreatedAt = now,
        UpdatedAt = now,

        BidCount = 1,
        // These are DateTime in your model in some places; using utc keeps Npgsql happy.
        CloseTime = utc.AddMinutes(-20),
        StartTime = utc.AddHours(-1),

        CurrentPriceCents = 1100,
        StartingPriceCents = 1000,
        ReserveMet = false,
      };
      db.Auctions.Add(auction);

      // Auction order awaiting payment
      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(), // doesn't matter for webhook test
        OrderNumber = "MK-TEST-STRIPE-AUCTION",
        SourceType = "AUCTION",
        AuctionId = auction.Id,
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(48),
        CurrencyCode = "USD",
        SubtotalCents = 1100,
        DiscountTotalCents = 0,
        TotalCents = 1100,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Orders.Add(order);

      // Order payment row (Stripe)
      var op = new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Provider = PaymentProviders.Stripe,
        Status = "REDIRECTED",
        ProviderCheckoutId = "cs_test_seed",
        ProviderPaymentId = null,
        AmountCents = order.TotalCents,
        CurrencyCode = order.CurrencyCode,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.OrderPayments.Add(op);

      await db.SaveChangesAsync();

      listingId = listing.Id;
      offerId = offer.Id;
      auctionId = auction.Id;
      orderId = order.Id;
      orderPaymentId = op.Id;
    }

    // Act: Stripe webhook (Testing shortcut uses X-Stripe-Event-Id)
    var stripeEventId = "evt_test_auction_stripe_001";
    var payload = new
    {
      type = "checkout.session.completed",
      data = new
      {
        @object = new
        {
          id = "cs_test_stripe_auction_smoke",
          payment_intent = "pi_test_stripe_auction_smoke",
          metadata = new
          {
            order_id = orderId.ToString(),
            order_payment_id = orderPaymentId.ToString()
          }
        }
      }
    };

    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
    {
      Content = JsonContent.Create(payload)
    };
    req.Headers.Add("X-Stripe-Event-Id", stripeEventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    // Assert
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = await db.Listings.SingleAsync(x => x.Id == listingId);
      listing.Status.Should().Be(ListingStatuses.Sold);
      listing.QuantityAvailable.Should().Be(0);

      var offer = await db.StoreOffers.SingleAsync(x => x.Id == offerId);
      offer.IsActive.Should().BeFalse();

      var auction = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auction.Status.Should().Be(AuctionStatuses.ClosedPaid);

      var order = await db.Orders.SingleAsync(x => x.Id == orderId);
      order.Status.Should().Be("READY_TO_FULFILL");
      order.PaidAt.Should().NotBeNull();

      var op = await db.OrderPayments.SingleAsync(x => x.Id == orderPaymentId);
      op.Status.Should().Be("SUCCEEDED");
      op.ProviderPaymentId.Should().NotBeNull();
    }
  }

  [Fact]
  public async Task PayPal_webhook_for_auction_order_marks_listing_sold_drains_inventory_and_disables_offers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    using var client = factory.CreateClient();

    var now = DateTimeOffset.UtcNow;
    var utc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    Guid listingId;
    Guid offerId;
    Guid auctionId;
    Guid orderId;
    Guid orderPaymentId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = new Listing
      {
        Id = Guid.NewGuid(),
        Title = "PayPal Auction Listing",
        Status = ListingStatuses.Published,
        QuantityTotal = 1,
        QuantityAvailable = 1,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Listings.Add(listing);

      var offer = new StoreOffer
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        PriceCents = 999,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.StoreOffers.Add(offer);

      var auction = new Auction
      {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Status = AuctionStatuses.ClosedWaitingOnPayment,
        CreatedAt = now,
        UpdatedAt = now,

        BidCount = 1,
        CloseTime = utc.AddMinutes(-20),
        StartTime = utc.AddHours(-1),

        CurrentPriceCents = 1100,
        StartingPriceCents = 1000,
        ReserveMet = false,
      };
      db.Auctions.Add(auction);

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        OrderNumber = "MK-TEST-PAYPAL-AUCTION",
        SourceType = "AUCTION",
        AuctionId = auction.Id,
        Status = "AWAITING_PAYMENT",
        PaymentDueAt = now.AddHours(48),
        CurrencyCode = "USD",
        SubtotalCents = 1100,
        DiscountTotalCents = 0,
        TotalCents = 1100,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.Orders.Add(order);

      var op = new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Provider = PaymentProviders.PayPal,
        Status = "REDIRECTED",
        ProviderCheckoutId = "PP-ORDER-ID-123",
        ProviderPaymentId = null,
        AmountCents = order.TotalCents,
        CurrencyCode = order.CurrencyCode,
        CreatedAt = now,
        UpdatedAt = now
      };
      db.OrderPayments.Add(op);

      await db.SaveChangesAsync();

      listingId = listing.Id;
      offerId = offer.Id;
      auctionId = auction.Id;
      orderId = order.Id;
      orderPaymentId = op.Id;
    }

    // Act: PayPal webhook payload (Testing shortcut uses PAYPAL-TRANSMISSION-ID)
    var paypalEventId = "pp_test_auction_paypal_001";

    var payload = new
    {
      event_type = "PAYMENT.CAPTURE.COMPLETED",
      resource = new
      {
        id = "CAPTURE_TEST_123",
        custom_id = orderPaymentId.ToString(), // correlates directly to OrderPayment.Id
        invoice_id = orderId.ToString(),       // correlates to Order.Id
        supplementary_data = new
        {
          related_ids = new
          {
            order_id = "PP-ORDER-ID-123"
          }
        }
      }
    };

    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
    {
      Content = JsonContent.Create(payload)
    };
    req.Headers.Add("PAYPAL-TRANSMISSION-ID", paypalEventId);

    var res = await client.SendAsync(req);
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    // Assert
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var listing = await db.Listings.SingleAsync(x => x.Id == listingId);
      listing.Status.Should().Be(ListingStatuses.Sold);
      listing.QuantityAvailable.Should().Be(0);

      var offer = await db.StoreOffers.SingleAsync(x => x.Id == offerId);
      offer.IsActive.Should().BeFalse();

      var auction = await db.Auctions.SingleAsync(x => x.Id == auctionId);
      auction.Status.Should().Be(AuctionStatuses.ClosedPaid);

      var order = await db.Orders.SingleAsync(x => x.Id == orderId);
      order.Status.Should().Be("READY_TO_FULFILL");
      order.PaidAt.Should().NotBeNull();

      var op = await db.OrderPayments.SingleAsync(x => x.Id == orderPaymentId);
      op.Status.Should().Be("SUCCEEDED");
      op.ProviderPaymentId.Should().Be("CAPTURE_TEST_123");
    }
  }
}
