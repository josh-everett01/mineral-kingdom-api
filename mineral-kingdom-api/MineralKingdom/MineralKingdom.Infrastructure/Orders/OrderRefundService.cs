using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Payments;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class OrderRefundService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IEnumerable<IOrderRefundProvider> _providers;

  public OrderRefundService(MineralKingdomDbContext db, IEnumerable<IOrderRefundProvider> providers)
  {
    _db = db;
    _providers = providers;
  }

  public async Task<(bool Ok, string? Error, OrderRefund? Refund)> AdminCreateRefundAsync(
    Guid orderId,
    long amountCents,
    string reason,
    string provider,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    if (amountCents <= 0) return (false, "AMOUNT_REQUIRED", null);
    if (amountCents > 100_000_000_000L) return (false, "AMOUNT_TOO_LARGE", null);

    if (string.IsNullOrWhiteSpace(reason)) return (false, "REASON_REQUIRED", null);
    reason = reason.Trim();
    if (reason.Length > 500) return (false, "REASON_TOO_LONG", null);

    if (string.IsNullOrWhiteSpace(provider)) return (false, "PROVIDER_REQUIRED", null);
    provider = provider.Trim().ToUpperInvariant();

    var p = _providers.FirstOrDefault(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    if (p is null) return (false, "PROVIDER_NOT_SUPPORTED", null);

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // lock order for consistent refund computations
    var order = await _db.Orders
      .FromSqlInterpolated($@"SELECT * FROM orders WHERE ""Id"" = {orderId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (order is null) return (false, "ORDER_NOT_FOUND", null);

    // guardrail: only refund paid/ready (or later). No refunds for awaiting payment/draft.
    if (!string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_REFUNDABLE", null);

    // compute remaining refundable
    var refundedSoFar = await _db.OrderRefunds.AsNoTracking()
      .Where(r => r.OrderId == orderId)
      .SumAsync(r => (long?)r.AmountCents, ct) ?? 0L;

    var remaining = (long)order.TotalCents - refundedSoFar;

    if (remaining <= 0) return (false, "ORDER_ALREADY_REFUNDED", null);
    if (amountCents > remaining) return (false, "REFUND_EXCEEDS_REMAINING", null);

    // call provider refund API (FAKE mode in tests/dev)
    var result = await p.RefundAsync(orderId, amountCents, order.CurrencyCode, reason, ct);

    var refund = new OrderRefund
    {
      Id = Guid.NewGuid(),
      OrderId = orderId,
      Provider = provider,
      ProviderRefundId = result.ProviderRefundId,
      AmountCents = amountCents,
      CurrencyCode = order.CurrencyCode,
      Reason = reason,
      CreatedAt = now
    };

    _db.OrderRefunds.Add(refund);

    // audit
    var action = amountCents == remaining ? "ORDER_REFUNDED_FULL" : "ORDER_REFUNDED_PARTIAL";

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = action,
      EntityType = "ORDER",
      EntityId = orderId,
      BeforeJson = $"{{\"refundedSoFar\":{refundedSoFar},\"remaining\":{remaining}}}",
      AfterJson = $"{{\"refundId\":\"{refund.Id}\",\"amountCents\":{amountCents},\"provider\":\"{provider}\",\"providerRefundId\":{System.Text.Json.JsonSerializer.Serialize(refund.ProviderRefundId)},\"reason\":{System.Text.Json.JsonSerializer.Serialize(reason)}}}",
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return (true, null, refund);
  }
}