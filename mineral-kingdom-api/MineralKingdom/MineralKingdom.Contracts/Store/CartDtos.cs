namespace MineralKingdom.Contracts.Store;

public static class CartStatuses
{
  public const string Active = "ACTIVE";
  public const string CheckedOut = "CHECKED_OUT";
  public const string Abandoned = "ABANDONED";
}

public static class CheckoutHoldStatuses
{
  public const string Active = "ACTIVE";
  public const string Expired = "EXPIRED";
  public const string Completed = "COMPLETED";
}

public sealed record CartLineDto(
  Guid OfferId,
  int Quantity
);

public sealed record CartDto(
  Guid CartId,
  Guid? UserId,
  string Status,
  IReadOnlyList<CartLineDto> Lines
);

public sealed record UpsertCartLineRequest(
  Guid OfferId,
  int Quantity
);

public sealed record StartCheckoutRequest(
  Guid? CartId, // 
  string? Email
);

public sealed record StartCheckoutResponse(
  Guid CartId,
  Guid HoldId,
  DateTimeOffset ExpiresAt
);

public sealed record CompleteCheckoutRequest(
  Guid HoldId,
  string PaymentReference // “fake” payment id for now; later becomes Stripe/whatever
);
