using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class PayPalCheckoutCaptureTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;

  public PayPalCheckoutCaptureTests(PostgresContainerFixture pg)
  {
    _pg = pg;
  }

  [Fact]
  public async Task Capture_paypal_payment_sets_provider_payment_id_and_returns_ok()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var seeded = await SeedPayPalCheckoutPaymentAsync(factory);

    var client = factory.CreateClient();

    var response = await client.PostAsync($"/api/payments/{seeded.PaymentId}/capture", content: null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var captureBody = await response.Content.ReadFromJsonAsync<CapturePaymentResponse>();
    captureBody.Should().NotBeNull();
    captureBody!.PaymentId.Should().Be(seeded.PaymentId);
    captureBody.Provider.Should().Be(PaymentProviders.PayPal);
    captureBody.PaymentStatus.Should().Be(CheckoutPaymentStatuses.Succeeded);
    captureBody.ProviderPaymentId.Should().NotBeNullOrWhiteSpace();

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var payment = await db.CheckoutPayments.SingleAsync(x => x.Id == seeded.PaymentId);
    payment.ProviderPaymentId.Should().NotBeNullOrWhiteSpace();
    payment.Status.Should().Be(CheckoutPaymentStatuses.Succeeded);
  }

  [Fact]
  public async Task Capture_nonexistent_payment_returns_not_found()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var client = factory.CreateClient();

    var response = await client.PostAsync($"/api/payments/{Guid.NewGuid()}/capture", content: null);

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<(Guid PaymentId, Guid HoldId, Guid CartId)> SeedPayPalCheckoutPaymentAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    var now = DateTimeOffset.UtcNow;

    var cart = new Cart
    {
      Id = Guid.NewGuid(),
      Status = CartStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now
    };

    var hold = new CheckoutHold
    {
      Id = Guid.NewGuid(),
      CartId = cart.Id,
      Status = CheckoutHoldStatuses.Active,
      CreatedAt = now,
      UpdatedAt = now,
      ExpiresAt = now.AddMinutes(10),
      ExtensionCount = 0
    };

    var payment = new CheckoutPayment
    {
      Id = Guid.NewGuid(),
      HoldId = hold.Id,
      CartId = cart.Id,
      Provider = PaymentProviders.PayPal,
      ProviderCheckoutId = "PAYPAL-ORDER-ID-TEST",
      Status = CheckoutPaymentStatuses.Redirected,
      AmountCents = 21900,
      CurrencyCode = "USD",
      CreatedAt = now,
      UpdatedAt = now
    };

    db.Carts.Add(cart);
    db.CheckoutHolds.Add(hold);
    db.CheckoutPayments.Add(payment);

    await db.SaveChangesAsync();

    return (payment.Id, hold.Id, cart.Id);
  }

  private sealed record CapturePaymentResponse(
    Guid PaymentId,
    string Provider,
    string PaymentStatus,
    string? ProviderPaymentId
  );
}