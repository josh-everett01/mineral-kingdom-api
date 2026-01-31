using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Api.Controllers;

[ApiController]
[Route("api/support")]
public sealed class SupportController : ControllerBase
{
  private readonly MineralKingdomDbContext _db;

  public SupportController(MineralKingdomDbContext db)
  {
    _db = db;
  }

  public sealed record CreateTicketRequest(
    string Email,
    string Subject,
    string Category,
    string Message,
    Guid? LinkedOrderId,
    Guid? LinkedAuctionId,
    Guid? LinkedShippingInvoiceId,
    Guid? LinkedListingId
  );

  [HttpPost("tickets")]
  [EnableRateLimiting("support")]
  public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.Email) ||
        string.IsNullOrWhiteSpace(req.Subject) ||
        string.IsNullOrWhiteSpace(req.Category) ||
        string.IsNullOrWhiteSpace(req.Message))
    {
      return BadRequest(new { error = "INVALID_INPUT" });
    }

    var ticket = new SupportTicket
    {
      Id = Guid.NewGuid(),
      Email = req.Email.Trim().ToLowerInvariant(),
      Subject = req.Subject.Trim(),
      Category = req.Category.Trim().ToUpperInvariant(),
      Message = req.Message.Trim(),
      LinkedOrderId = req.LinkedOrderId,
      LinkedAuctionId = req.LinkedAuctionId,
      LinkedShippingInvoiceId = req.LinkedShippingInvoiceId,
      LinkedListingId = req.LinkedListingId,
      Status = "OPEN",
      CreatedAt = DateTime.UtcNow
    };

    _db.SupportTickets.Add(ticket);
    await _db.SaveChangesAsync(ct);

    return Created($"/api/support/tickets/{ticket.Id}", new { ticketId = ticket.Id });
  }
}
