using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class AdminOrdersControllerTests
{
  private readonly PostgresContainerFixture _pg;

  public AdminOrdersControllerTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task List_Admin_Orders_Returns_Items_For_Owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner, now);
    var prefix = $"MK-ORDER-{Guid.NewGuid():N}".Substring(0, 18);

    var order1Number = $"{prefix}-1";
    var order2Number = $"{prefix}-2";

    var order1 = new Order
    {
      Id = Guid.NewGuid(),
      OrderNumber = order1Number,
      UserId = null,
      GuestEmail = "buyer1@example.com",
      SourceType = "STORE",
      Status = "READY_TO_FULFILL",
      CurrencyCode = "USD",
      SubtotalCents = 10000,
      DiscountTotalCents = 500,
      ShippingAmountCents = 1200,
      TotalCents = 10700,
      CreatedAt = now.AddHours(-2),
      UpdatedAt = now.AddHours(-2)
    };

    var order2 = new Order
    {
      Id = Guid.NewGuid(),
      OrderNumber = order2Number,
      UserId = null,
      GuestEmail = "buyer2@example.com",
      SourceType = "AUCTION",
      Status = "AWAITING_PAYMENT",
      CurrencyCode = "USD",
      SubtotalCents = 20000,
      DiscountTotalCents = 0,
      ShippingAmountCents = 2500,
      TotalCents = 22500,
      PaymentDueAt = now.AddDays(2),
      CreatedAt = now.AddHours(-1),
      UpdatedAt = now.AddHours(-1)
    };

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      db.Orders.AddRange(order1, order2);
      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var response = await client.GetAsync($"/api/admin/orders?q={prefix}");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrdersResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().HaveCount(2);
    body.Total.Should().Be(2);
    body.Items.Should().Contain(x => x.OrderNumber == order1Number);
    body.Items.Should().Contain(x => x.OrderNumber == order2Number);
  }

  [Fact]
  public async Task List_Admin_Orders_Filters_By_Status()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner, now);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.AddRange(
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-FILTER-1",
          GuestEmail = "filter1@example.com",
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          CurrencyCode = "USD",
          SubtotalCents = 10000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 11000,
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddHours(-2)
        },
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-FILTER-2",
          GuestEmail = "filter2@example.com",
          SourceType = "STORE",
          Status = "AWAITING_PAYMENT",
          CurrencyCode = "USD",
          SubtotalCents = 12000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 13000,
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddHours(-1)
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var response = await client.GetAsync("/api/admin/orders?status=AWAITING_PAYMENT");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrdersResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().HaveCount(1);
    body.Items[0].OrderNumber.Should().Be("MK-FILTER-2");
    body.Items[0].Status.Should().Be("AWAITING_PAYMENT");
  }

  [Fact]
  public async Task List_Admin_Orders_Searches_By_Order_Number()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner, now);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.AddRange(
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-SEARCH-ABC",
          GuestEmail = "search1@example.com",
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          CurrencyCode = "USD",
          SubtotalCents = 10000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 11000,
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddHours(-2)
        },
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-SEARCH-XYZ",
          GuestEmail = "search2@example.com",
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          CurrencyCode = "USD",
          SubtotalCents = 10000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 11000,
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddHours(-1)
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var response = await client.GetAsync("/api/admin/orders?q=ABC");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrdersResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().HaveCount(1);
    body.Items[0].OrderNumber.Should().Be("MK-SEARCH-ABC");
  }

  [Fact]
  public async Task List_Admin_Orders_Searches_By_Email()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var staff = await SeedAdminUserAsync(factory, UserRoles.Staff, now);

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.AddRange(
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-EMAIL-1",
          GuestEmail = "special-buyer@example.com",
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          CurrencyCode = "USD",
          SubtotalCents = 10000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 11000,
          CreatedAt = now.AddHours(-2),
          UpdatedAt = now.AddHours(-2)
        },
        new Order
        {
          Id = Guid.NewGuid(),
          OrderNumber = "MK-EMAIL-2",
          GuestEmail = "other@example.com",
          SourceType = "STORE",
          Status = "READY_TO_FULFILL",
          CurrencyCode = "USD",
          SubtotalCents = 10000,
          DiscountTotalCents = 0,
          ShippingAmountCents = 1000,
          TotalCents = 11000,
          CreatedAt = now.AddHours(-1),
          UpdatedAt = now.AddHours(-1)
        });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, staff);

    var response = await client.GetAsync("/api/admin/orders?q=special-buyer@example.com");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrdersResponseDto>();
    body.Should().NotBeNull();
    body!.Items.Should().HaveCount(1);
    body.Items[0].CustomerEmail.Should().Be("special-buyer@example.com");
  }

  [Fact]
  public async Task Get_Admin_Order_Detail_Returns_Refund_History_And_CanRefund_For_Owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner, now);
    var orderId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.Add(new Order
      {
        Id = orderId,
        OrderNumber = "MK-DETAIL-1",
        UserId = null,
        GuestEmail = "detail@example.com",
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 20000,
        DiscountTotalCents = 0,
        ShippingAmountCents = 2500,
        TotalCents = 22500,
        PaidAt = now.AddHours(-1),
        CreatedAt = now.AddHours(-2),
        UpdatedAt = now.AddHours(-1)
      });

      db.OrderPayments.Add(new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = PaymentProviders.Stripe,
        Status = CheckoutPaymentStatuses.Succeeded,
        AmountCents = 22500,
        CurrencyCode = "USD",
        ProviderCheckoutId = "cs_test_123",
        ProviderPaymentId = "pi_test_123",
        CreatedAt = now.AddHours(-1),
        UpdatedAt = now.AddHours(-1)
      });

      db.OrderRefunds.Add(new OrderRefund
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = PaymentProviders.Stripe,
        AmountCents = 5000,
        CurrencyCode = "USD",
        ProviderRefundId = "re_test_123",
        Reason = "Customer request",
        CreatedAt = now
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var response = await client.GetAsync($"/api/admin/orders/{orderId}");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrderDetailDto>();
    body.Should().NotBeNull();

    body!.OrderNumber.Should().Be("MK-DETAIL-1");
    body.CustomerEmail.Should().Be("detail@example.com");
    body.Payments.Should().HaveCount(1);
    body.RefundHistory.Should().HaveCount(1);
    body.TotalRefundedCents.Should().Be(5000);
    body.RemainingRefundableCents.Should().Be(17500);
    body.IsFullyRefunded.Should().BeFalse();
    body.IsPartiallyRefunded.Should().BeTrue();
    body.CanRefund.Should().BeTrue();
    body.AvailableRefundProviders.Should().Contain(PaymentProviders.Stripe);
  }

  [Fact]
  public async Task Get_Admin_Order_Detail_Returns_CanRefund_False_For_Staff()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var staff = await SeedAdminUserAsync(factory, UserRoles.Staff, now);
    var orderId = Guid.NewGuid();

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

      db.Orders.Add(new Order
      {
        Id = orderId,
        OrderNumber = "MK-DETAIL-STAFF",
        UserId = null,
        GuestEmail = "staff-view@example.com",
        SourceType = "STORE",
        Status = "READY_TO_FULFILL",
        CurrencyCode = "USD",
        SubtotalCents = 15000,
        DiscountTotalCents = 0,
        ShippingAmountCents = 1500,
        TotalCents = 16500,
        PaidAt = now.AddHours(-1),
        CreatedAt = now.AddHours(-2),
        UpdatedAt = now.AddHours(-1)
      });

      db.OrderPayments.Add(new OrderPayment
      {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = PaymentProviders.PayPal,
        Status = CheckoutPaymentStatuses.Succeeded,
        AmountCents = 16500,
        CurrencyCode = "USD",
        ProviderCheckoutId = "paypal_checkout_123",
        ProviderPaymentId = "paypal_payment_123",
        CreatedAt = now.AddHours(-1),
        UpdatedAt = now.AddHours(-1)
      });

      await db.SaveChangesAsync();
    }

    using var client = factory.CreateClient();
    AddAdminHeaders(client, staff);

    var response = await client.GetAsync($"/api/admin/orders/{orderId}");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<AdminOrderDetailDto>();
    body.Should().NotBeNull();

    body!.CanRefund.Should().BeFalse();
    body.AvailableRefundProviders.Should().Contain(PaymentProviders.PayPal);
  }

  [Fact]
  public async Task Get_Admin_Order_Detail_Returns_NotFound_For_Missing_Order()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var now = DateTimeOffset.UtcNow;
    var owner = await SeedAdminUserAsync(factory, UserRoles.Owner, now);

    using var client = factory.CreateClient();
    AddAdminHeaders(client, owner);

    var response = await client.GetAsync($"/api/admin/orders/{Guid.NewGuid()}");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static void AddAdminHeaders(HttpClient client, User admin)
  {
    client.DefaultRequestHeaders.Remove("X-Test-UserId");
    client.DefaultRequestHeaders.Remove("X-Test-EmailVerified");
    client.DefaultRequestHeaders.Remove("X-Test-Role");

    client.DefaultRequestHeaders.Add("X-Test-UserId", admin.Id.ToString());
    client.DefaultRequestHeaders.Add("X-Test-EmailVerified", "true");
    client.DefaultRequestHeaders.Add("X-Test-Role", admin.Role);
  }

  private static async Task<User> SeedAdminUserAsync(TestAppFactory factory, string role, DateTimeOffset now)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var admin = new User
    {
      Id = Guid.NewGuid(),
      Email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
      PasswordHash = "x",
      EmailVerified = true,
      Role = role,
      CreatedAt = now.UtcDateTime,
      UpdatedAt = now.UtcDateTime
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();

    return admin;
  }
}