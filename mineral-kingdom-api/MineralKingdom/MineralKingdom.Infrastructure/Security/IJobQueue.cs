using System;
using System.Threading;
using System.Threading.Tasks;

namespace MineralKingdom.Infrastructure.Security;

public interface IJobQueue
{
  Task<Guid> EnqueueAsync(
    string type,
    object? payload,
    DateTimeOffset? runAt = null,
    int? maxAttempts = null,
    CancellationToken ct = default);

  Task<bool> MarkSucceededAsync(Guid jobId, CancellationToken ct = default);
}
