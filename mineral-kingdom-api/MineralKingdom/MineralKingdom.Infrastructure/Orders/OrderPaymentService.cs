using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Services;

public sealed class OrderPaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IEnumerable<IOrderPaymentProvider> _providers;
  private readonly TimeProvider _time;

  public OrderPaymentService(
    MineralKingdomDbContext db,
    IEnumerable<IOrderPaymentProvider> providers,
    TimeProvider time)
  {
    _db = db;
    _providers = providers;
    _time = time;
  }

  public async Task<StartOrderPaymentResponse> StartAuctionOrderPaymentAsync(
    Guid orderId,
    Guid userId,
    StartOrderPaymentRequest req,
    CancellationToken ct)
  {
    var now = _time.GetUtcNow();

    var order = await _db.Orders
      .AsNoTracking()
      .Where(o => o.Id == orderId)
      .Select(o => new
      {
        o.Id,
        o.UserId,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.PaymentDueAt
      })
      .SingleOrDefaultAsync(ct);

    if (order is null)
      throw new InvalidOperationException("Order not found.");

    if (order.UserId != userId)
      throw new InvalidOperationException("Order not found."); // avoid leaking existence

    if (!string.Equals(order.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Order is not an auction order.");

    if (!string.Equals(order.Status, "AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("ORDER_NOT_AWAITING_PAYMENT");

    if (order.PaymentDueAt is not null && now > order.PaymentDueAt.Value)
      throw new InvalidOperationException("Payment window expired.");

    var provider = (req.Provider ?? "").Trim();
    var p = _providers.SingleOrDefault(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    if (p is null)
      throw new InvalidOperationException("UNSUPPORTED_PROVIDER");

    // Canonicalize to the provider's exact constant value (e.g. "STRIPE", "PAYPAL")
    provider = p.Provider;



    // Create payment row first so we can correlate via metadata/custom_id
    var op = new OrderPayment
    {
      Id = Guid.NewGuid(),
      OrderId = order.Id,
      Provider = provider,
      Status = "CREATED",
      AmountCents = order.TotalCents,
      CurrencyCode = string.IsNullOrWhiteSpace(order.CurrencyCode) ? "USD" : order.CurrencyCode,
      CreatedAt = now,
      UpdatedAt = now
    };

    _db.OrderPayments.Add(op);
    await _db.SaveChangesAsync(ct);

    // For now, one line item is fine for auctions (single listing at final price)
    var redirect = await p.CreateRedirectAsync(new CreateOrderPaymentRedirectRequest(
      OrderId: order.Id,
      OrderPaymentId: op.Id,
      AmountCents: op.AmountCents,
      CurrencyCode: op.CurrencyCode,
      SuccessUrl: req.SuccessUrl,
      CancelUrl: req.CancelUrl,
      LineItems: new[]
      {
        new OrderPaymentLineItem("Auction order", 1, op.AmountCents)
      }
    ), ct);

    op.Status = "REDIRECTED";
    op.ProviderCheckoutId = redirect.ProviderCheckoutId;
    op.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return new StartOrderPaymentResponse(op.Id, op.Provider, op.Status, redirect.RedirectUrl);
  }
}
