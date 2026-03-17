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

public static class CartNoticeTypes
{
  public const string ItemRemovedSold = "ITEM_REMOVED_SOLD";
}

public sealed record CartNoticeDto(
  Guid Id,
  string Type,
  string Message,
  Guid? OfferId,
  Guid? ListingId,
  DateTimeOffset CreatedAt,
  DateTimeOffset? DismissedAt
);

public sealed record CartLineDto(
  Guid OfferId,
  Guid ListingId,
  string ListingHref,
  string Title,
  string? PrimaryImageUrl,
  int Quantity,
  int QuantityAvailable,
  int PriceCents,
  int EffectivePriceCents,
  bool CanUpdateQuantity
);

public sealed record CartDto(
  Guid CartId,
  Guid? UserId,
  string Status,
  int SubtotalCents,
  IReadOnlyList<string> Warnings,
  IReadOnlyList<CartNoticeDto> Notices,
  IReadOnlyList<CartLineDto> Lines
);

public sealed record UpsertCartLineRequest(
  Guid OfferId,
  int Quantity
);

public sealed record DismissCartNoticeResponse(
  bool Dismissed,
  Guid NoticeId
);

public sealed record StartCheckoutRequest(
  Guid? CartId,
  string? Email
);

public sealed record StartCheckoutResponse(
  Guid CartId,
  Guid HoldId,
  DateTimeOffset ExpiresAt
);

public sealed record ActiveCheckoutResponse(
  bool Active,
  Guid CartId,
  Guid? HoldId,
  DateTimeOffset? ExpiresAt,
  string? GuestEmail,
  string? Status,
  bool CanExtend,
  int ExtensionCount,
  int MaxExtensions
);

public sealed record ResetCheckoutResponse(
  bool Reset,
  Guid CartId
);

public sealed record CompleteCheckoutRequest(
  Guid HoldId,
  string PaymentReference
);

public sealed record ExtendCheckoutRequest(
  Guid HoldId
);

public sealed record ExtendCheckoutResponse(
  Guid HoldId,
  DateTimeOffset ExpiresAt,
  bool CanExtend,
  int ExtensionCount,
  int MaxExtensions
);