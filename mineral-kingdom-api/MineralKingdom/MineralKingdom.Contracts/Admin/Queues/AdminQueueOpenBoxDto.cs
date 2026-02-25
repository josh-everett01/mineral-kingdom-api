namespace MineralKingdom.Contracts.Admin.Queues;

public sealed record AdminQueueOpenBoxDto(
  Guid FulfillmentGroupId,
  Guid? UserId,
  string? GuestEmail,
  string Status,
  DateTimeOffset UpdatedAt,
  int OrderCount);