using System.Text.Json;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class AuditLogger : IAuditLogger
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  private readonly MineralKingdomDbContext _db;

  public AuditLogger(MineralKingdomDbContext db) => _db = db;

  private static string? Clamp(string? s, int max)
    => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

  public Task LogAsync(AuditEvent evt, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(evt.ActionType))
      throw new ArgumentException("ActionType is required.", nameof(evt));

    if (string.IsNullOrWhiteSpace(evt.EntityType))
      throw new ArgumentException("EntityType is required.", nameof(evt));

    var row = new AdminAuditLog
    {
      Id = Guid.NewGuid(),
      ActorUserId = evt.ActorUserId,
      ActorRole = string.IsNullOrWhiteSpace(evt.ActorRole) ? null : evt.ActorRole.Trim().ToUpperInvariant(),
      ActionType = evt.ActionType.Trim().ToUpperInvariant(),
      EntityType = evt.EntityType.Trim().ToUpperInvariant(),
      EntityId = evt.EntityId,
      BeforeJson = evt.Before is null ? null : JsonSerializer.Serialize(evt.Before, JsonOptions),
      AfterJson = evt.After is null ? null : JsonSerializer.Serialize(evt.After, JsonOptions),
      IpAddress = Clamp(evt.IpAddress, 64),
      UserAgent = Clamp(evt.UserAgent, 512),
      CreatedAt = DateTimeOffset.UtcNow
    };

    _db.AdminAuditLogs.Add(row);
    return Task.CompletedTask;
  }
}

