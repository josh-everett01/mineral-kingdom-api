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
      throw new InvalidOperationException("Order not found.");

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

    provider = p.Provider;

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

  public async Task<OrderPaymentConfirmationResponse?> GetConfirmationAsync(
    Guid paymentId,
    CancellationToken ct)
  {
    var payment = await _db.OrderPayments
      .AsNoTracking()
      .Where(p => p.Id == paymentId)
      .Select(p => new
      {
        p.Id,
        p.Provider,
        p.Status,
        p.OrderId
      })
      .SingleOrDefaultAsync(ct);

    if (payment is null)
      return null;

    var order = await _db.Orders
      .AsNoTracking()
      .Where(o => o.Id == payment.OrderId)
      .Select(o => new
      {
        o.Id,
        o.OrderNumber,
        o.Status,
        o.TotalCents,
        o.CurrencyCode
      })
      .SingleOrDefaultAsync(ct);

    var isConfirmed =
      string.Equals(payment.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
      order is not null &&
      string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase);

    return new OrderPaymentConfirmationResponse(
      PaymentId: payment.Id,
      Provider: payment.Provider,
      PaymentStatus: payment.Status,
      IsConfirmed: isConfirmed,
      OrderId: order?.Id,
      OrderNumber: order?.OrderNumber,
      OrderStatus: order?.Status,
      OrderTotalCents: order?.TotalCents,
      OrderCurrencyCode: order?.CurrencyCode
    );
  }
}