using System;
using System.Threading;
using System.Threading.Tasks;

namespace MineralKingdom.Infrastructure.Security;

public interface IAuditLogger
{
  Task LogAsync(AuditEvent evt, CancellationToken ct = default);
}

public sealed record AuditEvent(
    Guid ActorUserId,
    string? ActorRole,
    string ActionType,
    string EntityType,
    Guid EntityId,
    object? Before = null,
    object? After = null,
    string? IpAddress = null,
    string? UserAgent = null
);
