using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Orders;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Services;

public sealed class OrderPaymentService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IEnumerable<IOrderPaymentProvider> _providers;
  private readonly TimeProvider _time;
  private readonly OrderService _orders;

  public OrderPaymentService(
    MineralKingdomDbContext db,
    IEnumerable<IOrderPaymentProvider> providers,
    TimeProvider time,
    OrderService orders)
  {
    _db = db;
    _providers = providers;
    _time = time;
    _orders = orders;
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
          o.ShippingMode,
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

    if (string.Equals(order.ShippingMode, "UNSELECTED", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("AUCTION_SHIPPING_CHOICE_REQUIRED");

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
      Status = OrderPaymentStatuses.Created,
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

    op.Status = OrderPaymentStatuses.Redirected;
    op.ProviderCheckoutId = redirect.ProviderCheckoutId;
    op.UpdatedAt = now;

    await _db.SaveChangesAsync(ct);

    return new StartOrderPaymentResponse(op.Id, op.Provider, op.Status, redirect.RedirectUrl);
  }

  public async Task<(bool Ok, string? Error, OrderPayment? Payment)> CaptureAsync(
    Guid paymentId,
    Guid userId,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var payment = await _db.OrderPayments.SingleOrDefaultAsync(p => p.Id == paymentId, ct);
    if (payment is null)
      return (false, "PAYMENT_NOT_FOUND", null);

    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == payment.OrderId, ct);
    if (order is null || order.UserId != userId)
      return (false, "PAYMENT_NOT_FOUND", null);

    if (!string.Equals(payment.Provider, PaymentProviders.PayPal, StringComparison.OrdinalIgnoreCase))
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", payment);

    if (string.Equals(payment.Status, OrderPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
    {
      return (true, null, payment);
    }

    if (string.IsNullOrWhiteSpace(payment.ProviderCheckoutId))
      return (false, "PROVIDER_CHECKOUT_ID_MISSING", payment);

    if (string.Equals(payment.Status, OrderPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
    {
      var confirmExisting = await _orders.ConfirmPaidOrderFromWebhookAsync(
        order.Id,
        payment.ProviderPaymentId ?? payment.ProviderCheckoutId ?? payment.Id.ToString(),
        now,
        ct);

      if (!confirmExisting.Ok)
        return (false, "ORDER_CONFIRMATION_FAILED", payment);

      payment.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
      return (true, null, payment);
    }

    var paypalProvider = _providers.OfType<PayPalOrderPaymentProvider>().SingleOrDefault();
    if (paypalProvider is null)
      return (false, "PROVIDER_CAPTURE_NOT_SUPPORTED", payment);

    try
    {
      var capture = await paypalProvider.CaptureOrderAsync(payment.ProviderCheckoutId, ct);

      payment.ProviderCheckoutId = capture.ProviderCheckoutId;
      payment.UpdatedAt = now;

      if (!string.IsNullOrWhiteSpace(capture.CaptureId))
      {
        payment.ProviderPaymentId = capture.CaptureId;
      }

      var isCaptureConfirmed =
        string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(capture.Status, "ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase);

      if (!isCaptureConfirmed)
        return (false, "PAYPAL_CAPTURE_FAILED", payment);

      var confirm = await _orders.ConfirmPaidOrderFromWebhookAsync(
        order.Id,
        capture.CaptureId ?? capture.ProviderCheckoutId,
        now,
        ct);

      if (!confirm.Ok)
        return (false, "ORDER_CONFIRMATION_FAILED", payment);

      payment.Status = OrderPaymentStatuses.Succeeded;
      payment.UpdatedAt = now;

      await _db.SaveChangesAsync(ct);

      return (true, null, payment);
    }
    catch (InvalidOperationException ex) when (
      ex.Message.StartsWith("PAYPAL_NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_CAPTURE_ORDER_FAILED", StringComparison.OrdinalIgnoreCase) ||
      ex.Message.StartsWith("PAYPAL_", StringComparison.OrdinalIgnoreCase))
    {
      payment.UpdatedAt = now;
      await _db.SaveChangesAsync(ct);
      return (false, "PAYPAL_CAPTURE_FAILED", payment);
    }
  }

  public async Task<Guid?> GetPaymentOwnerUserIdAsync(Guid paymentId, CancellationToken ct)
  {
    return await _db.OrderPayments
      .AsNoTracking()
      .Where(p => p.Id == paymentId)
      .Join(
        _db.Orders.AsNoTracking(),
        payment => payment.OrderId,
        order => order.Id,
        (_, order) => order.UserId)
      .SingleOrDefaultAsync(ct);
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
      string.Equals(payment.Status, OrderPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) &&
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