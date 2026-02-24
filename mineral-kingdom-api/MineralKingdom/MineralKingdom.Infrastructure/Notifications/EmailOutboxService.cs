using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Infrastructure.Notifications;

public sealed class EmailOutboxService
{
  private readonly MineralKingdomDbContext _db;
  private readonly IJobQueue _jobs;

  public EmailOutboxService(MineralKingdomDbContext db, IJobQueue jobs)
  {
    _db = db;
    _jobs = jobs;
  }

  public async Task<(bool Inserted, EmailOutbox? Row)> EnqueueAsync(
    string toEmail,
    string templateKey,
    string payloadJson,
    string dedupeKey,
    DateTimeOffset now,
    CancellationToken ct)
  {
    var row = new EmailOutbox
    {
      Id = Guid.NewGuid(),
      ToEmail = toEmail.Trim().ToLowerInvariant(),
      TemplateKey = templateKey,
      PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
      DedupeKey = dedupeKey,
      Status = "PENDING",
      Attempts = 0,
      MaxAttempts = 8,
      LastError = null,
      CreatedAt = now,
      UpdatedAt = now,
      SentAt = null
    };

    _db.EmailOutbox.Add(row);

    try
    {
      await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      // DedupeKey conflict: already queued/sent
      return (false, null);
    }

    await _jobs.EnqueueAsync(
      type: "EMAIL_DISPATCH",
      payload: new { emailOutboxId = row.Id },
      runAt: now,
      maxAttempts: row.MaxAttempts,
      ct: ct);

    return (true, row);
  }
}