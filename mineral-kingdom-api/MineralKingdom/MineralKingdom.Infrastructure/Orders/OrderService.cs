using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auctions;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Store;
using MineralKingdom.Infrastructure.Notifications;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Store;

namespace MineralKingdom.Infrastructure.Orders;

public sealed class OrderService
{
  private readonly MineralKingdomDbContext _db;
  private readonly EmailOutboxService _emails;
  private readonly OrderSnapshotService _orderSnapshots;

  public OrderService(
    MineralKingdomDbContext db,
    EmailOutboxService emails,
    OrderSnapshotService orderSnapshots)
  {
    _db = db;
    _emails = emails;
    _orderSnapshots = orderSnapshots;
  }

  public sealed record CreateLine(Guid OfferId, int Quantity);

  public async Task<(bool Ok, string? Error, Order? Order)> CreateDraftAsync(
    Guid userId,
    List<CreateLine> lines,
    CancellationToken ct)
  {
    if (lines is null || lines.Count == 0)
      return (false, "NO_LINES", null);

    if (lines.Any(l => l.Quantity <= 0 || l.Quantity > 99))
      return (false, "INVALID_QUANTITY", null);

    var now = DateTimeOffset.UtcNow;

    var offerIds = lines.Select(x => x.OfferId).Distinct().ToList();

    var offers = await _db.StoreOffers
      .AsNoTracking()
      .Where(x => offerIds.Contains(x.Id) && x.DeletedAt == null && x.IsActive)
      .ToListAsync(ct);

    if (offers.Count != offerIds.Count)
      return (false, "OFFER_NOT_FOUND", null);

    var order = new Order
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      GuestEmail = null,
      OrderNumber = GenerateOrderNumber(now),
      CheckoutHoldId = null,
      Status = "DRAFT",
      PaidAt = null,
      CurrencyCode = "USD",
      CreatedAt = now,
      UpdatedAt = now
    };

    var offerById = offers.ToDictionary(x => x.Id, x => x);

    foreach (var reqLine in lines)
    {
      var offer = offerById[reqLine.OfferId];

      if (!StoreOfferService.IsOfferCurrentlyValid(offer, now))
        return (false, "OFFER_NOT_ACTIVE", null);

      var unitPrice = offer.PriceCents;
      var unitDiscountRaw = StoreOfferService.ComputeUnitDiscountCents(offer);
      var unitDiscount = Math.Clamp(unitDiscountRaw, 0, unitPrice);
      var unitFinal = unitPrice - unitDiscount;

      var qty = reqLine.Quantity;

      var lineSubtotal = checked((int)((long)unitPrice * qty));
      var lineDiscount = checked((int)((long)unitDiscount * qty));
      var lineTotal = checked((int)((long)unitFinal * qty));

      var line = new OrderLine
      {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        OfferId = offer.Id,
        ListingId = offer.ListingId,
        UnitPriceCents = unitPrice,
        UnitDiscountCents = unitDiscount,
        UnitFinalPriceCents = unitFinal,
        Quantity = qty,
        LineSubtotalCents = lineSubtotal,
        LineDiscountCents = lineDiscount,
        LineTotalCents = lineTotal,
        CreatedAt = now,
        UpdatedAt = now
      };

      order.Lines.Add(line);
    }

    order.SubtotalCents = checked(order.Lines.Sum(x => x.LineSubtotalCents));
    order.DiscountTotalCents = checked(order.Lines.Sum(x => x.LineDiscountCents));
    order.TotalCents = checked(order.Lines.Sum(x => x.LineTotalCents));

    _db.Orders.Add(order);
    await _db.SaveChangesAsync(ct);

    return (true, null, order);
  }

  public async Task<(bool Ok, string? Error, Order? Order)> GetAsync(
    Guid orderId,
    Guid userId,
    CancellationToken ct)
  {
    var order = await _db.Orders
      .Include(o => o.Lines)
      .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    if (order is null) return (false, "ORDER_NOT_FOUND", null);
    if (order.UserId != userId) return (false, "FORBIDDEN", null);

    return (true, null, order);
  }

  public async Task<OrderDto?> GetGuestOrderAsync(
    string orderNumber,
    string email,
    CancellationToken ct)
  {
    var orderId = await _db.Orders
      .AsNoTracking()
      .Where(o => o.OrderNumber == orderNumber && o.GuestEmail == email)
      .Select(o => (Guid?)o.Id)
      .SingleOrDefaultAsync(ct);

    if (orderId is null)
      return null;

    return await _orderSnapshots.GetOrderAsync(orderId.Value, ct);
  }

  private static string GenerateOrderNumber(DateTimeOffset now)
  {
    var date = now.ToString("yyyyMMdd");
    var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    return $"MK-{date}-{suffix}";
  }

  public async Task<(bool Ok, string? Error)> AdminExtendAuctionPaymentDueAsync(
    Guid orderId,
    DateTimeOffset newDueAt,
    Guid actorUserId,
    DateTimeOffset now,
    string? ipAddress,
    string? userAgent,
    CancellationToken ct)
  {
    var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
    if (order is null) return (false, "ORDER_NOT_FOUND");

    if (!string.Equals(order.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase))
      return (false, "NOT_AUCTION_ORDER");

    if (!string.Equals(order.Status, "AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_AWAITING_PAYMENT");

    if (order.PaymentDueAt is null) return (false, "PAYMENT_DUE_MISSING");

    if (newDueAt == default) return (false, "PAYMENT_DUE_REQUIRED");
    if (newDueAt <= now) return (false, "PAYMENT_DUE_MUST_BE_IN_FUTURE");

    var oldDue = order.PaymentDueAt.Value;

    if (newDueAt < oldDue) return (false, "PAYMENT_DUE_CANNOT_DECREASE");
    if (newDueAt == oldDue) return (true, null);

    var maxDue = now.AddDays(30);
    if (newDueAt > maxDue) return (false, "PAYMENT_DUE_TOO_FAR_IN_FUTURE");

    order.PaymentDueAt = newDueAt;
    order.UpdatedAt = now;

    _db.AdminAuditLogs.Add(new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = actorUserId,
      ActorRole = UserRoles.Owner,
      ActionType = "ORDER_PAYMENT_DUE_EXTENDED",
      EntityType = "ORDER",
      EntityId = order.Id,
      BeforeJson = $"{{\"paymentDueAt\":\"{oldDue:O}\"}}",
      AfterJson = $"{{\"paymentDueAt\":\"{newDueAt:O}\"}}",
      IpAddress = ipAddress,
      UserAgent = userAgent,
      CreatedAt = now
    });

    await _db.SaveChangesAsync(ct);
    return (true, null);
  }

  public async Task<(bool Ok, string? Error)> ConfirmPaidOrderFromWebhookAsync(
    Guid orderId,
    string paymentReference,
    DateTimeOffset now,
    CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var order = await _db.Orders
      .FromSqlInterpolated($@"SELECT * FROM orders WHERE ""Id"" = {orderId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (order is null) return (false, "ORDER_NOT_FOUND");

    if (string.Equals(order.Status, "READY_TO_FULFILL", StringComparison.OrdinalIgnoreCase))
    {
      await tx.CommitAsync(ct);

      try
      {
        string? toEmail = null;

        if (order.UserId is Guid uid)
        {
          toEmail = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(ct);
        }
        else
        {
          toEmail = order.GuestEmail;
        }

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
          var payload =
            $"{{\"orderId\":\"{order.Id}\",\"orderNumber\":\"{order.OrderNumber}\",\"totalCents\":{order.TotalCents},\"currency\":\"{order.CurrencyCode}\"}}";

          await _emails.EnqueueAsync(
            toEmail: toEmail,
            templateKey: EmailTemplateKeys.PaymentReceived,
            payloadJson: payload,
            dedupeKey: EmailDedupeKeys.PaymentReceived(order.Id, toEmail),
            now: now,
            ct: ct);
        }
      }
      catch
      {
        // best-effort
      }

      return (true, null);
    }

    if (!string.Equals(order.Status, "AWAITING_PAYMENT", StringComparison.OrdinalIgnoreCase))
      return (false, "ORDER_NOT_AWAITING_PAYMENT");

    order.Status = "READY_TO_FULFILL";
    order.PaidAt = now;
    order.UpdatedAt = now;

    if (string.Equals(order.SourceType, "AUCTION", StringComparison.OrdinalIgnoreCase) && order.AuctionId.HasValue)
    {
      var auction = await _db.Auctions
        .FromSqlInterpolated($@"SELECT * FROM auctions WHERE ""Id"" = {order.AuctionId.Value} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

      if (auction is not null &&
          string.Equals(auction.Status, AuctionStatuses.ClosedWaitingOnPayment, StringComparison.OrdinalIgnoreCase))
      {
        auction.Status = AuctionStatuses.ClosedPaid;
        auction.UpdatedAt = now;
      }
    }

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return (true, null);
  }
}