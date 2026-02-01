namespace MineralKingdom.Worker.Jobs;

public interface IJobHandler
{
  string Type { get; }
  Task ExecuteAsync(Guid jobId, string? payloadJson, CancellationToken ct);
}

public sealed class JobHandlerRegistry
{
  private readonly Dictionary<string, IJobHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

  public void Register(IJobHandler handler)
  {
    _handlers[handler.Type] = handler;
  }

  public bool TryGet(string type, out IJobHandler handler)
    => _handlers.TryGetValue(type, out handler!);
}