using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Admin.Queues;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Infrastructure.Admin.Queues;

public sealed class AdminQueuesService
{
  private readonly MineralKingdomDbContext _db;
  private const int Limit = 50; // admin queue view default

  public AdminQueuesService(MineralKingdomDbContext db) => _db = db;

  public Task<List<AdminQueueOrderDto>> GetOrdersAwaitingPaymentAsync(CancellationToken ct)
  {
    return _db.Orders.AsNoTracking()
      .Where(o => o.SourceType == "AUCTION" && o.Status == "AWAITING_PAYMENT")
      .OrderBy(o => o.PaymentDueAt)
      .ThenByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new AdminQueueOrderDto(
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId))
      .ToListAsync(ct);
  }

  public Task<List<AdminQueueOrderDto>> GetOrdersReadyToFulfillAsync(CancellationToken ct)
  {
    return _db.Orders.AsNoTracking()
      .Where(o => o.Status == "READY_TO_FULFILL")
      .OrderByDescending(o => o.CreatedAt)
      .Take(Limit)
      .Select(o => new AdminQueueOrderDto(
        o.Id,
        o.OrderNumber,
        o.SourceType,
        o.Status,
        o.TotalCents,
        o.CurrencyCode,
        o.CreatedAt,
        o.PaymentDueAt,
        o.FulfillmentGroupId))
      .ToListAsync(ct);
  }

  public async Task<List<AdminQueueFulfillmentGroupDto>> GetFulfillmentPackedAsync(CancellationToken ct)
  {
    // PACKED groups that are CLOSED boxes (ready to ship ops typically)
    var groups = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.Status == "PACKED")
      .OrderByDescending(g => g.UpdatedAt)
      .Take(Limit)
      .Select(g => new
      {
        g.Id,
        g.BoxStatus,
        g.Status,
        g.ShippingCarrier,
        g.TrackingNumber,
        g.UpdatedAt
      })
      .ToListAsync(ct);

    var ids = groups.Select(g => g.Id).ToList();

    var counts = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId != null && ids.Contains(o.FulfillmentGroupId.Value))
      .GroupBy(o => o.FulfillmentGroupId!.Value)
      .Select(g => new { GroupId = g.Key, Count = g.Count() })
      .ToListAsync(ct);

    var countById = counts.ToDictionary(x => x.GroupId, x => x.Count);

    return groups.Select(g => new AdminQueueFulfillmentGroupDto(
      g.Id,
      g.BoxStatus,
      g.Status,
      g.ShippingCarrier,
      g.TrackingNumber,
      g.UpdatedAt,
      countById.TryGetValue(g.Id, out var c) ? c : 0
    )).ToList();
  }

  public async Task<List<AdminQueueFulfillmentGroupDto>> GetFulfillmentShippedAsync(CancellationToken ct)
  {
    var groups = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.Status == "SHIPPED")
      .OrderByDescending(g => g.UpdatedAt)
      .Take(Limit)
      .Select(g => new
      {
        g.Id,
        g.BoxStatus,
        g.Status,
        g.ShippingCarrier,
        g.TrackingNumber,
        g.UpdatedAt
      })
      .ToListAsync(ct);

    var ids = groups.Select(g => g.Id).ToList();

    var counts = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId != null && ids.Contains(o.FulfillmentGroupId.Value))
      .GroupBy(o => o.FulfillmentGroupId!.Value)
      .Select(g => new { GroupId = g.Key, Count = g.Count() })
      .ToListAsync(ct);

    var countById = counts.ToDictionary(x => x.GroupId, x => x.Count);

    return groups.Select(g => new AdminQueueFulfillmentGroupDto(
      g.Id,
      g.BoxStatus,
      g.Status,
      g.ShippingCarrier,
      g.TrackingNumber,
      g.UpdatedAt,
      countById.TryGetValue(g.Id, out var c) ? c : 0
    )).ToList();
  }

  public async Task<List<AdminQueueOpenBoxDto>> GetOpenBoxesAsync(CancellationToken ct)
  {
    var boxes = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => g.BoxStatus == "OPEN")
      .OrderByDescending(g => g.UpdatedAt)
      .Take(Limit)
      .Select(g => new
      {
        g.Id,
        g.UserId,
        g.GuestEmail,
        g.Status,
        g.UpdatedAt
      })
      .ToListAsync(ct);

    var ids = boxes.Select(b => b.Id).ToList();

    var counts = await _db.Orders.AsNoTracking()
      .Where(o => o.FulfillmentGroupId != null && ids.Contains(o.FulfillmentGroupId.Value))
      .GroupBy(o => o.FulfillmentGroupId!.Value)
      .Select(g => new { GroupId = g.Key, Count = g.Count() })
      .ToListAsync(ct);

    var countById = counts.ToDictionary(x => x.GroupId, x => x.Count);

    return boxes.Select(b => new AdminQueueOpenBoxDto(
      b.Id,
      b.UserId,
      b.GuestEmail,
      b.Status,
      b.UpdatedAt,
      countById.TryGetValue(b.Id, out var c) ? c : 0
    )).ToList();
  }
}