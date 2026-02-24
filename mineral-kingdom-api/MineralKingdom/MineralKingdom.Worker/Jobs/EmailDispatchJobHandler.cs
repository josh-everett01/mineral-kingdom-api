using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Services;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;

namespace MineralKingdom.Worker.Jobs;

public sealed class EmailDispatchJobHandler : IJobHandler
{
  private readonly MineralKingdomDbContext _db;
  private readonly IMKEmailSender _email;

  public EmailDispatchJobHandler(MineralKingdomDbContext db, IMKEmailSender email)
  {
    _db = db;
    _email = email;
  }

  public string Type => "EMAIL_DISPATCH";

  public async Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(payloadJson))
      throw new InvalidOperationException("INVALID_EMAIL_OUTBOX_JOB_PAYLOAD");

    using var doc = JsonDocument.Parse(payloadJson);
    if (!doc.RootElement.TryGetProperty("emailOutboxId", out var idProp))
      throw new InvalidOperationException("INVALID_EMAIL_OUTBOX_JOB_PAYLOAD");

    var idStr = idProp.GetString();
    if (!Guid.TryParse(idStr, out var outboxId))
      throw new InvalidOperationException("INVALID_EMAIL_OUTBOX_JOB_PAYLOAD");

    var now = DateTimeOffset.UtcNow;

    // Lock row for idempotency under retries / double-processing protection
    var row = await _db.EmailOutbox
      .FromSqlInterpolated($@"SELECT * FROM email_outbox WHERE ""Id"" = {outboxId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

    if (row is null)
      return; // nothing to do

    // Idempotent: already sent
    if (string.Equals(row.Status, "SENT", StringComparison.OrdinalIgnoreCase))
      return;

    try
    {
      // v1: generic email dispatch
      // Later we’ll add template rendering per TemplateKey.
      var subject = $"Mineral Kingdom: {row.TemplateKey}";
      var body = row.PayloadJson;

      await _email.SendGenericTransactionalAsync(row.ToEmail, subject, body, ct);

      row.Status = "SENT";
      row.SentAt = now;
      row.LastError = null;
      row.UpdatedAt = now;
    }
    catch (Exception ex)
    {
      // Record error details but let job runner handle retries/DLQ.
      row.Status = "FAILED";
      row.LastError = ex.Message;
      row.Attempts += 1;
      row.UpdatedAt = now;

      await _db.SaveChangesAsync(ct);
      throw;
    }

    await _db.SaveChangesAsync(ct);
  }
}