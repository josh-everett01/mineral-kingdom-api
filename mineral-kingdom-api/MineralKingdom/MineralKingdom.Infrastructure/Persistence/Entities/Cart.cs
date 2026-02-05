using System;
using System.Collections.Generic;

namespace MineralKingdom.Infrastructure.Persistence.Entities;

public sealed class Cart
{
  public Guid Id { get; set; }

  public Guid? UserId { get; set; } // null for guest carts

  public string Status { get; set; } = "ACTIVE";

  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }

  public List<CartLine> Lines { get; set; } = new();
}
