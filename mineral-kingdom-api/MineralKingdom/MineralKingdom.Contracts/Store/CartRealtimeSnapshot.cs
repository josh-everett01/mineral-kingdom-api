namespace MineralKingdom.Contracts.Store;

public sealed record CartRealtimeSnapshot(
  Guid CartId,
  string Status,
  int SubtotalCents,
  int LineCount,
  int NoticeCount,
  DateTimeOffset EmittedAt
);