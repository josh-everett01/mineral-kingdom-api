using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Orders;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class AdminOrderQueryService
{
  private readonly MineralKingdomDbContext _db;

  public AdminOrderQueryService(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public async Task<AdminOrdersResponseDto> GetAdminOrdersAsync(
    string? status,
    string? q,
    CancellationToken ct)
  {
    var query =
      from order in _db.Orders.AsNoTracking()
      join user in _db.Users.AsNoTracking() on order.UserId equals user.Id into userJoin
      from user in userJoin.DefaultIfEmpty()
      select new
      {
        order.Id,
        order.OrderNumber,
        order.Status,
        order.SourceType,
        CustomerEmail = order.UserId != null ? user!.Email : order.GuestEmail,
        order.CurrencyCode,
        order.SubtotalCents,
        order.DiscountTotalCents,
        order.ShippingAmountCents,
        order.TotalCents,
        order.PaymentDueAt,
        order.PaidAt,
        order.CreatedAt,
        order.UpdatedAt
      };

    if (!string.IsNullOrWhiteSpace(status))
    {
      var normalizedStatus = status.Trim().ToUpperInvariant();
      query = query.Where(x => x.Status == normalizedStatus);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
      var term = q.Trim().ToLowerInvariant();
      query = query.Where(x =>
        x.OrderNumber.ToLower().Contains(term) ||
        (x.CustomerEmail != null && x.CustomerEmail.ToLower().Contains(term)));
    }

    var rows = await query
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync(ct);

    var orderIds = rows.Select(x => x.Id).ToList();

    var refundsByOrderId = await _db.OrderRefunds.AsNoTracking()
      .Where(x => orderIds.Contains(x.OrderId))
      .GroupBy(x => x.OrderId)
      .Select(g => new
      {
        OrderId = g.Key,
        TotalRefundedCents = g.Sum(x => x.AmountCents)
      })
      .ToListAsync(ct);

    var refundedLookup = refundsByOrderId.ToDictionary(x => x.OrderId, x => x.TotalRefundedCents);

    var items = rows.Select(x =>
    {
      var totalRefundedCents = refundedLookup.GetValueOrDefault(x.Id, 0L);
      var remainingRefundableCents = Math.Max(0L, (long)x.TotalCents - totalRefundedCents);
      var isFullyRefunded = totalRefundedCents > 0 && remainingRefundableCents == 0;
      var isPartiallyRefunded = totalRefundedCents > 0 && remainingRefundableCents > 0;

      return new AdminOrderListItemDto(
        x.Id,
        x.OrderNumber,
        x.Status,
        x.SourceType,
        x.CustomerEmail,
        x.CurrencyCode,
        x.SubtotalCents,
        x.DiscountTotalCents,
        x.ShippingAmountCents,
        x.TotalCents,
        x.PaymentDueAt,
        x.PaidAt,
        x.CreatedAt,
        x.UpdatedAt,
        totalRefundedCents,
        remainingRefundableCents,
        isFullyRefunded,
        isPartiallyRefunded
      );
    }).ToList();

    return new AdminOrdersResponseDto(
      items,
      items.Count
    );
  }

  public async Task<AdminOrderDetailDto?> GetAdminOrderDetailAsync(
    Guid id,
    bool canRefund,
    CancellationToken ct)
  {
    var orderRow = await (
      from order in _db.Orders.AsNoTracking()
      join user in _db.Users.AsNoTracking() on order.UserId equals user.Id into userJoin
      from user in userJoin.DefaultIfEmpty()
      where order.Id == id
      select new
      {
        order.Id,
        order.OrderNumber,
        order.Status,
        order.SourceType,
        order.UserId,
        order.GuestEmail,
        CustomerEmail = order.UserId != null ? user!.Email : order.GuestEmail,
        order.CurrencyCode,
        order.SubtotalCents,
        order.DiscountTotalCents,
        order.ShippingAmountCents,
        order.TotalCents,
        order.PaymentDueAt,
        order.PaidAt,
        order.AuctionId,
        order.ShippingMode,
        order.CreatedAt,
        order.UpdatedAt
      })
      .SingleOrDefaultAsync(ct);

    if (orderRow is null)
      return null;

    var payments = await _db.OrderPayments.AsNoTracking()
      .Where(x => x.OrderId == id)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new AdminOrderPaymentSummaryDto(
        x.Provider,
        x.Status,
        x.AmountCents,
        x.CurrencyCode,
        x.ProviderPaymentId,
        x.ProviderCheckoutId,
        x.CreatedAt,
        x.UpdatedAt
      ))
      .ToListAsync(ct);

    var refundHistory = await _db.OrderRefunds.AsNoTracking()
      .Where(x => x.OrderId == id)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new AdminOrderRefundHistoryItemDto(
        x.Id,
        x.AmountCents,
        x.CurrencyCode,
        x.Provider,
        x.ProviderRefundId,
        x.Reason,
        x.CreatedAt
      ))
      .ToListAsync(ct);

    var totalRefundedCents = refundHistory.Sum(x => x.AmountCents);
    var remainingRefundableCents = Math.Max(0L, (long)orderRow.TotalCents - totalRefundedCents);
    var isFullyRefunded = totalRefundedCents > 0 && remainingRefundableCents == 0;
    var isPartiallyRefunded = totalRefundedCents > 0 && remainingRefundableCents > 0;

    var availableRefundProviders = payments
      .Where(x => string.Equals(x.Status, CheckoutPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
      .Select(x => x.Provider)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var effectiveCanRefund =
      canRefund &&
      remainingRefundableCents > 0 &&
      availableRefundProviders.Count > 0;

    return new AdminOrderDetailDto(
      orderRow.Id,
      orderRow.OrderNumber,
      orderRow.Status,
      orderRow.SourceType,
      orderRow.UserId,
      orderRow.GuestEmail,
      orderRow.CustomerEmail,
      orderRow.CurrencyCode,
      orderRow.SubtotalCents,
      orderRow.DiscountTotalCents,
      orderRow.ShippingAmountCents,
      orderRow.TotalCents,
      orderRow.PaymentDueAt,
      orderRow.PaidAt,
      orderRow.AuctionId,
      orderRow.ShippingMode,
      totalRefundedCents,
      remainingRefundableCents,
      isFullyRefunded,
      isPartiallyRefunded,
      effectiveCanRefund,
      availableRefundProviders,
      payments,
      refundHistory,
      orderRow.CreatedAt,
      orderRow.UpdatedAt
    );
  }
}