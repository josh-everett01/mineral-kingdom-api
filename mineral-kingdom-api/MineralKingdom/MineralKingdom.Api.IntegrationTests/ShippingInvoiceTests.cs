using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Listings;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class ShippingInvoiceTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public ShippingInvoiceTests(PostgresContainerFixture pg) => _pg = pg;

  private static void AsUser(HttpClient client, Guid userId, string role, bool emailVerified = true)
  {
    client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId.ToString());
    client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, emailVerified ? "true" : "false");
    client.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, role);
  }

  [Fact]
  public async Task Closing_open_box_generates_shipping_invoice_using_tiers()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid order1Id;
    Guid order2Id;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_inv_user1@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      // Two orders totaling 3000 cents => should hit fallback tier 0-4999 => 599
      var o1 = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-SI-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      };

      var o2 = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-SI-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 2000,
        DiscountTotalCents = 0,
        TotalCents = 2000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.AddRange(o1, o2);
      await db.SaveChangesAsync();

      order1Id = o1.Id;
      order2Id = o2.Id;
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    // Create open box
    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);

    // Add both orders
    (await client.PostAsync($"/api/me/open-box/orders/{order1Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.PostAsync($"/api/me/open-box/orders/{order2Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Close box => should generate invoice
    (await client.PostAsync("/api/me/open-box/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Assert invoice exists
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var box = await db.FulfillmentGroups.AsNoTracking()
        .SingleAsync(g => g.UserId == userId && g.BoxStatus == "CLOSED");

      var inv = await db.ShippingInvoices.AsNoTracking()
        .Where(i => i.FulfillmentGroupId == box.Id)
        .OrderByDescending(i => i.CreatedAt)
        .FirstOrDefaultAsync();

      inv.Should().NotBeNull();
      inv!.Status.Should().Be("UNPAID");
      inv.CurrencyCode.Should().Be("USD");
      inv.AmountCents.Should().Be(599);
    }
  }

  [Fact]
  public async Task Cannot_ship_when_shipping_invoice_unpaid_and_amount_gt_0()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_inv_user2@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-SI-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 6000,
        DiscountTotalCents = 0,
        TotalCents = 6000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      await db.SaveChangesAsync();
      orderId = order.Id;
    }

    // Customer creates/assigns/closes box => invoice created UNPAID
    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostAsync($"/api/me/open-box/orders/{orderId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.PostAsync("/api/me/open-box/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Admin packs then tries to ship => should be blocked SHIPPING_UNPAID
    using var admin = factory.CreateClient();
    AsUser(admin, Guid.NewGuid(), UserRoles.Owner);

    (await admin.PostAsync($"/api/admin/orders/{orderId}/fulfillment/packed", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var ship = await admin.PostAsJsonAsync($"/api/admin/orders/{orderId}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "X" });

    ship.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var body = await ship.Content.ReadAsStringAsync();
    body.Should().Contain("SHIPPING_UNPAID");
  }

  [Fact]
  public async Task Can_ship_when_shipping_invoice_paid_or_amount_is_zero()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;
    Guid boxId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_inv_user3@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-SI-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 1000,
        DiscountTotalCents = 0,
        TotalCents = 1000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      await db.SaveChangesAsync();
      orderId = order.Id;
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostAsync($"/api/me/open-box/orders/{orderId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.PostAsync("/api/me/open-box/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Locate invoice + mark paid directly (simulating verified webhook for DoD gating test)
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var box = await db.FulfillmentGroups.AsNoTracking()
        .SingleAsync(g => g.UserId == userId && g.BoxStatus == "CLOSED");
      boxId = box.Id;

      var inv = await db.ShippingInvoices
        .SingleAsync(i => i.FulfillmentGroupId == boxId);

      inv.Status = "PAID";
      inv.PaidAt = DateTimeOffset.UtcNow;
      inv.UpdatedAt = DateTimeOffset.UtcNow;

      await db.SaveChangesAsync();
    }

    using var admin = factory.CreateClient();
    AsUser(admin, Guid.NewGuid(), UserRoles.Owner);

    (await admin.PostAsync($"/api/admin/orders/{orderId}/fulfillment/packed", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var ship = await admin.PostAsJsonAsync($"/api/admin/orders/{orderId}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "OK" });

    ship.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task Admin_override_to_zero_marks_invoice_paid_and_allows_shipping()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    Guid userId;
    Guid orderId;
    Guid boxId;
    Guid invoiceId;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      userId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_inv_user4@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
      });

      var now = DateTimeOffset.UtcNow;

      var order = new Order
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"MK-SI-{Guid.NewGuid():N}"[..18],
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 15000,
        DiscountTotalCents = 0,
        TotalCents = 15000,
        CreatedAt = now,
        UpdatedAt = now
      };

      db.Orders.Add(order);
      await db.SaveChangesAsync();
      orderId = order.Id;
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    (await client.PostAsync("/api/me/open-box", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostAsync($"/api/me/open-box/orders/{orderId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.PostAsync("/api/me/open-box/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Find invoice and override it directly through service-like behavior (we’ll add admin endpoint later)
    await using (var scope2 = factory.Services.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var box = await db.FulfillmentGroups.AsNoTracking()
        .SingleAsync(g => g.UserId == userId && g.BoxStatus == "CLOSED");
      boxId = box.Id;

      var inv = await db.ShippingInvoices.SingleAsync(i => i.FulfillmentGroupId == boxId);
      invoiceId = inv.Id;
    }

    await using (var scope3 = factory.Services.CreateAsyncScope())
    {
      var svc = scope3.ServiceProvider.GetRequiredService<MineralKingdom.Infrastructure.Payments.ShippingInvoiceService>();
      var ok = await svc.AdminOverrideShippingAsync(
        boxId,
        amountCents: 0,
        reason: "free shipping",
        actorUserId: Guid.NewGuid(),
        now: DateTimeOffset.UtcNow,
        ipAddress: null,
        userAgent: null,
        ct: CancellationToken.None);

      ok.Ok.Should().BeTrue();
    }

    // Invoice should now be PAID
    await using (var scope4 = factory.Services.CreateAsyncScope())
    {
      var db = scope4.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var inv = await db.ShippingInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
      inv.Status.Should().Be("PAID");
      inv.IsOverride.Should().BeTrue();
      inv.OverrideReason.Should().Be("free shipping");
    }

    using var admin = factory.CreateClient();
    AsUser(admin, Guid.NewGuid(), UserRoles.Owner);

    (await admin.PostAsync($"/api/admin/orders/{orderId}/fulfillment/packed", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var ship = await admin.PostAsJsonAsync($"/api/admin/orders/{orderId}/fulfillment/shipped",
      new AdminMarkShippedRequest { ShippingCarrier = "USPS", TrackingNumber = "OK" });

    ship.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task Current_shipping_invoice_detail_includes_related_orders_items_and_preview_context()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var mineralName = $"Quartz-{Guid.NewGuid():N}";

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var mineralId = Guid.NewGuid();
      var listingId = Guid.NewGuid();
      var orderId = Guid.NewGuid();
      var groupId = Guid.NewGuid();
      var invoiceId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_invoice_detail@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Minerals.Add(new Mineral
      {
        Id = mineralId,
        Name = mineralName,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Quartz Cluster",
        Description = "Open Box item",
        Status = "PUBLISHED",
        PrimaryMineralId = mineralId,
        LocalityDisplay = "Arkansas, USA",
        CreatedAt = now,
        UpdatedAt = now
      });

      db.ListingMedia.Add(new ListingMedia
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Url = "https://cdn.example.com/quartz.jpg",
        Status = ListingMediaStatuses.Ready,
        MediaType = "IMAGE",
        IsPrimary = true,
        SortOrder = 0,
        ContentLengthBytes = 123,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = userId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        ClosedAt = now,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        OrderNumber = "MK-20260331-SHIP01",
        SourceType = "AUCTION",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 11000,
        DiscountTotalCents = 0,
        TotalCents = 11000,
        FulfillmentGroupId = groupId,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.OrderLines.Add(new OrderLine
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        OfferId = null,
        ListingId = listingId,
        Quantity = 1,
        UnitPriceCents = 11000,
        UnitDiscountCents = 0,
        UnitFinalPriceCents = 11000,
        LineSubtotalCents = 11000,
        LineDiscountCents = 0,
        LineTotalCents = 11000,
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
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    var res = await client.GetAsync("/api/me/open-box/shipping-invoice");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ShippingInvoiceDetailDto>();
    dto.Should().NotBeNull();

    dto!.AmountCents.Should().Be(599);
    dto.Status.Should().Be("UNPAID");
    dto.ItemCount.Should().Be(1);
    dto.PreviewTitle.Should().Be("Quartz Cluster");
    dto.PreviewImageUrl.Should().Be("https://cdn.example.com/quartz.jpg");

    dto.RelatedOrders.Should().HaveCount(1);
    dto.RelatedOrders[0].OrderNumber.Should().Be("MK-20260331-SHIP01");
    dto.RelatedOrders[0].SourceType.Should().Be("AUCTION");

    dto.Items.Should().HaveCount(1);
    dto.Items[0].Title.Should().Be("Quartz Cluster");
    dto.Items[0].OrderNumber.Should().Be("MK-20260331-SHIP01");
    dto.Items[0].SourceType.Should().Be("AUCTION");
    dto.Items[0].PrimaryImageUrl.Should().Be("https://cdn.example.com/quartz.jpg");
    dto.Items[0].MineralName.Should().Be(mineralName);
    dto.Items[0].Locality.Should().Be("Arkansas, USA");
    dto.Items[0].Quantity.Should().Be(1);
  }

  [Fact]
  public async Task Open_box_shipping_invoice_endpoint_returns_not_found_when_user_has_active_open_box_even_if_old_closed_box_invoice_exists()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "open_box_invoice_guardrail@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      var closedGroupId = Guid.NewGuid();
      var openGroupId = Guid.NewGuid();

      db.FulfillmentGroups.AddRange(
        new FulfillmentGroup
        {
          Id = closedGroupId,
          UserId = userId,
          GuestEmail = null,
          BoxStatus = "CLOSED",
          ClosedAt = now.AddDays(-2),
          Status = "READY_TO_FULFILL",
          CreatedAt = now.AddDays(-3),
          UpdatedAt = now.AddDays(-2)
        },
        new FulfillmentGroup
        {
          Id = openGroupId,
          UserId = userId,
          GuestEmail = null,
          BoxStatus = "OPEN",
          ClosedAt = null,
          Status = "READY_TO_FULFILL",
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now
        });

      db.ShippingInvoices.Add(new ShippingInvoice
      {
        Id = Guid.NewGuid(),
        FulfillmentGroupId = closedGroupId,
        AmountCents = 599,
        CalculatedAmountCents = 599,
        CurrencyCode = "USD",
        Status = "PAID",
        PaidAt = now.AddDays(-2),
        CreatedAt = now.AddDays(-2),
        UpdatedAt = now.AddDays(-2)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    var res = await client.GetAsync("/api/me/open-box/shipping-invoice");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);

    var body = await res.Content.ReadAsStringAsync();
    body.Should().Contain("NO_INVOICE_FOR_OPEN_BOX");
  }

  [Fact]
  public async Task Shipping_invoice_detail_by_id_returns_owned_invoice()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var userId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var invoiceId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var mineralId = Guid.NewGuid();
      var listingId = Guid.NewGuid();
      var orderId = Guid.NewGuid();
      var groupId = Guid.NewGuid();

      db.Users.Add(new User
      {
        Id = userId,
        Email = "ship_invoice_detail_by_id@example.com",
        EmailVerified = true,
        Role = UserRoles.User,
        CreatedAt = now.UtcDateTime,
        UpdatedAt = now.UtcDateTime
      });

      db.Minerals.Add(new Mineral
      {
        Id = mineralId,
        Name = "Quartz",
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Listings.Add(new Listing
      {
        Id = listingId,
        Title = "Quartz Cluster",
        Description = "Specific invoice detail",
        Status = "PUBLISHED",
        PrimaryMineralId = mineralId,
        LocalityDisplay = "Arkansas, USA",
        CreatedAt = now,
        UpdatedAt = now
      });

      db.ListingMedia.Add(new ListingMedia
      {
        Id = Guid.NewGuid(),
        ListingId = listingId,
        Url = "https://cdn.example.com/quartz.jpg",
        Status = ListingMediaStatuses.Ready,
        MediaType = "IMAGE",
        IsPrimary = true,
        SortOrder = 0,
        ContentLengthBytes = 123,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = userId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        ClosedAt = now,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.Orders.Add(new Order
      {
        Id = orderId,
        UserId = userId,
        OrderNumber = "MK-20260404-SHIP01",
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        PaidAt = now,
        CurrencyCode = "USD",
        SubtotalCents = 11000,
        DiscountTotalCents = 0,
        TotalCents = 11000,
        FulfillmentGroupId = groupId,
        CreatedAt = now,
        UpdatedAt = now
      });

      db.OrderLines.Add(new OrderLine
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        OfferId = null,
        ListingId = listingId,
        Quantity = 1,
        UnitPriceCents = 11000,
        UnitDiscountCents = 0,
        UnitFinalPriceCents = 11000,
        LineSubtotalCents = 11000,
        LineDiscountCents = 0,
        LineTotalCents = 11000,
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
        Provider = "STRIPE",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, userId, UserRoles.User);

    var res = await client.GetAsync($"/api/shipping-invoices/{invoiceId}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await res.Content.ReadFromJsonAsync<ShippingInvoiceDetailDto>();
    dto.Should().NotBeNull();

    dto!.ShippingInvoiceId.Should().Be(invoiceId);
    dto.AmountCents.Should().Be(599);
    dto.Status.Should().Be("UNPAID");
    dto.ItemCount.Should().Be(1);
    dto.PreviewTitle.Should().Be("Quartz Cluster");
    dto.Items.Should().HaveCount(1);
    dto.RelatedOrders.Should().HaveCount(1);
  }

  [Fact]
  public async Task Shipping_invoice_detail_by_id_returns_not_found_for_other_user()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);

    var ownerUserId = Guid.NewGuid();
    var otherUserId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var invoiceId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      var groupId = Guid.NewGuid();

      db.Users.AddRange(
        new User
        {
          Id = ownerUserId,
          Email = "ship_invoice_owner@example.com",
          EmailVerified = true,
          Role = UserRoles.User,
          CreatedAt = now.UtcDateTime,
          UpdatedAt = now.UtcDateTime
        },
        new User
        {
          Id = otherUserId,
          Email = "ship_invoice_other@example.com",
          EmailVerified = true,
          Role = UserRoles.User,
          CreatedAt = now.UtcDateTime,
          UpdatedAt = now.UtcDateTime
        });

      db.FulfillmentGroups.Add(new FulfillmentGroup
      {
        Id = groupId,
        UserId = ownerUserId,
        Status = "READY_TO_FULFILL",
        BoxStatus = "CLOSED",
        ClosedAt = now,
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
        Provider = "STRIPE",
        CreatedAt = now,
        UpdatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AsUser(client, otherUserId, UserRoles.User);

    var res = await client.GetAsync($"/api/shipping-invoices/{invoiceId}");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);

    var body = await res.Content.ReadAsStringAsync();
    body.Should().Contain("INVOICE_NOT_FOUND");
  }
}