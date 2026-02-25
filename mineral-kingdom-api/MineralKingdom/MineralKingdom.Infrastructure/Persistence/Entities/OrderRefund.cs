using System.ComponentModel.DataAnnotations;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class OrderRefund
{
  public Guid Id { get; set; }

  public Guid OrderId { get; set; }

  [MaxLength(20)]
  public string Provider { get; set; } = null!;

  [MaxLength(200)]
  public string? ProviderRefundId { get; set; }

  public long AmountCents { get; set; }

  [MaxLength(10)]
  public string CurrencyCode { get; set; } = null!;

  [MaxLength(500)]
  public string? Reason { get; set; }

  public DateTimeOffset CreatedAt { get; set; }

  public Order Order { get; set; } = null!;
}