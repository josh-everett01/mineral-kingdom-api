using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/admin/exports")]
[Authorize(Roles = $"{UserRoles.Owner},{UserRoles.Staff}")]
public sealed class AdminExportsController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;
  public AdminExportsController(MineralKingdomDbContext db) => _db = db;

  [HttpGet("orders.csv")]
  public async Task<IActionResult> Orders([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    var start = new DateTimeOffset(fromDate.Date, TimeSpan.Zero);
    var end = new DateTimeOffset(toDate.Date.AddDays(1), TimeSpan.Zero);

    var rows = await _db.Orders.AsNoTracking()
      .Where(o => o.PaidAt != null && o.PaidAt >= start && o.PaidAt < end)
      .OrderBy(o => o.PaidAt)
      .Select(o => new
      {
        o.OrderNumber,
        PaidAt = o.PaidAt,
        CustomerEmail = o.GuestEmail,
        o.SourceType,
        o.SubtotalCents,
        o.DiscountTotalCents,
        o.TotalCents,
        o.Status
      })
      .ToListAsync(ct);

    var sb = new StringBuilder();
    sb.AppendLine("order_number,date_paid,customer_email,source,subtotal_cents,discount_total_cents,total_cents,status");

    foreach (var r in rows)
    {
      sb.AppendCsv(r.OrderNumber);
      sb.Append(',');
      sb.AppendCsv(r.PaidAt?.UtcDateTime.ToString("O") ?? "");
      sb.Append(',');
      sb.AppendCsv(r.CustomerEmail ?? "");
      sb.Append(',');
      sb.AppendCsv(r.SourceType);
      sb.Append(',');
      sb.Append(r.SubtotalCents);
      sb.Append(',');
      sb.Append(r.DiscountTotalCents);
      sb.Append(',');
      sb.Append(r.TotalCents);
      sb.Append(',');
      sb.AppendCsv(r.Status);
      sb.AppendLine();
    }

    return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"orders_{from}_{to}.csv");
  }

  [HttpGet("auctions.csv")]
  public async Task<IActionResult> Auctions([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    var start = new DateTimeOffset(fromDate.Date, TimeSpan.Zero);
    var end = new DateTimeOffset(toDate.Date.AddDays(1), TimeSpan.Zero);

    var rows = await _db.Auctions.AsNoTracking()
      .Where(a => a.CloseTime >= start && a.CloseTime < end)
      .OrderBy(a => a.CloseTime)
      .Select(a => new
      {
        a.Id,
        a.CloseTime,
        a.CurrentPriceCents,
        a.BidCount,
        a.ReserveMet,
        a.Status,
        a.ListingId
      })
      .ToListAsync(ct);

    // listing titles in a second query to avoid heavy joins
    var listingIds = rows.Select(r => r.ListingId).Distinct().ToList();
    var listingTitles = await _db.Listings.AsNoTracking()
      .Where(l => listingIds.Contains(l.Id))
      .ToDictionaryAsync(l => l.Id, l => l.Title ?? "", ct);

    var sb = new StringBuilder();
    sb.AppendLine("auction_id,listing_title,close_time,final_price_cents,bid_count,reserve_met,status");

    foreach (var r in rows)
    {
      sb.AppendCsv(r.Id.ToString());
      sb.Append(',');
      sb.AppendCsv(listingTitles.TryGetValue(r.ListingId, out var title) ? title : "");
      sb.Append(',');
      sb.AppendCsv(r.CloseTime.UtcDateTime.ToString("O"));
      sb.Append(',');
      sb.Append(r.CurrentPriceCents);
      sb.Append(',');
      sb.Append(r.BidCount);
      sb.Append(',');
      sb.Append(r.ReserveMet ? "true" : "false");
      sb.Append(',');
      sb.AppendCsv(r.Status);
      sb.AppendLine();
    }

    return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"auctions_{from}_{to}.csv");
  }

  [HttpGet("invoices.csv")]
  public async Task<IActionResult> Invoices([FromQuery] string from, [FromQuery] string to, CancellationToken ct)
  {
    if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
      return BadRequest(new { error = "INVALID_DATE_RANGE" });

    var start = new DateTimeOffset(fromDate.Date, TimeSpan.Zero);
    var end = new DateTimeOffset(toDate.Date.AddDays(1), TimeSpan.Zero);

    var rows = await _db.ShippingInvoices.AsNoTracking()
      .Where(i => i.CreatedAt >= start && i.CreatedAt < end)
      .OrderBy(i => i.CreatedAt)
      .Select(i => new
      {
        i.Id,
        i.FulfillmentGroupId,
        i.AmountCents,
        i.CurrencyCode,
        i.Status,
        i.CreatedAt,
        i.PaidAt
      })
      .ToListAsync(ct);

    var groupIds = rows.Select(r => r.FulfillmentGroupId).Distinct().ToList();
    var groups = await _db.FulfillmentGroups.AsNoTracking()
      .Where(g => groupIds.Contains(g.Id))
      .Select(g => new { g.Id, g.UserId, g.GuestEmail })
      .ToListAsync(ct);

    // user emails
    var userIds = groups.Where(g => g.UserId != null).Select(g => g.UserId!.Value).Distinct().ToList();
    var userEmails = await _db.Users.AsNoTracking()
      .Where(u => userIds.Contains(u.Id))
      .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

    var groupEmail = groups.ToDictionary(
      g => g.Id,
      g => g.GuestEmail ?? (g.UserId != null && userEmails.TryGetValue(g.UserId.Value, out var e) ? e : "")
    );

    var sb = new StringBuilder();
    sb.AppendLine("invoice_id,customer_email,amount_cents,currency,status,created_at,paid_at");

    foreach (var r in rows)
    {
      sb.AppendCsv(r.Id.ToString());
      sb.Append(',');
      sb.AppendCsv(groupEmail.TryGetValue(r.FulfillmentGroupId, out var email) ? email : "");
      sb.Append(',');
      sb.Append(r.AmountCents);
      sb.Append(',');
      sb.AppendCsv(r.CurrencyCode);
      sb.Append(',');
      sb.AppendCsv(r.Status);
      sb.Append(',');
      sb.AppendCsv(r.CreatedAt.UtcDateTime.ToString("O"));
      sb.Append(',');
      sb.AppendCsv(r.PaidAt?.UtcDateTime.ToString("O") ?? "");
      sb.AppendLine();
    }

    return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"invoices_{from}_{to}.csv");
  }

  private static bool TryParseDate(string s, out DateTime dt)
  {
    return DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt);
  }
}

internal static class CsvExtensions
{
  public static void AppendCsv(this StringBuilder sb, string value)
  {
    value ??= "";
    var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
    if (!mustQuote)
    {
      sb.Append(value);
      return;
    }

    sb.Append('"');
    sb.Append(value.Replace("\"", "\"\""));
    sb.Append('"');
  }
}