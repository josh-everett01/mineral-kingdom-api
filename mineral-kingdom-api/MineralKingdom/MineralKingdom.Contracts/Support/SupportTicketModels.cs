namespace MineralKingdom.Contracts.Support;

public static class SupportTicketCategories
{
  public const string OrderHelp = "ORDER_HELP";
  public const string AuctionHelp = "AUCTION_HELP";
  public const string ShippingHelp = "SHIPPING_HELP";
  public const string OpenBoxHelp = "OPEN_BOX_HELP";
  public const string PaymentHelp = "PAYMENT_HELP";
  public const string Other = "OTHER";

  public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
  {
    OrderHelp, AuctionHelp, ShippingHelp, OpenBoxHelp, PaymentHelp, Other
  };
}

public static class SupportTicketPriorities
{
  public const string Low = "LOW";
  public const string Normal = "NORMAL";
  public const string High = "HIGH";
  public const string Urgent = "URGENT";

  public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
  {
    Low, Normal, High, Urgent
  };
}

public static class SupportTicketStatuses
{
  public const string Open = "OPEN";
  public const string WaitingOnCustomer = "WAITING_ON_CUSTOMER";
  public const string WaitingOnSupport = "WAITING_ON_SUPPORT";
  public const string Resolved = "RESOLVED";
  public const string Closed = "CLOSED";

  public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
  {
    Open, WaitingOnCustomer, WaitingOnSupport, Resolved, Closed
  };
}

public sealed record SupportTicketMessageDto(
  Guid Id,
  string AuthorType, // CUSTOMER|SUPPORT
  Guid? AuthorUserId,
  string BodyText,
  bool IsInternalNote,
  DateTimeOffset CreatedAt
);

public sealed record SupportTicketDto(
  Guid Id,
  string TicketNumber,
  Guid? CreatedByUserId,
  string? GuestEmail,
  string Subject,
  string Category,
  string Priority,
  string Status,
  Guid? AssignedToUserId,
  Guid? LinkedOrderId,
  Guid? LinkedAuctionId,
  Guid? LinkedShippingInvoiceId,
  Guid? LinkedListingId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? ClosedAt,
  IReadOnlyList<SupportTicketMessageDto> Messages
);

public sealed record CreateSupportTicketRequest(
  string? Email, // required for guests; ignored for members
  string Subject,
  string Category,
  string Message,
  Guid? LinkedOrderId,
  Guid? LinkedAuctionId,
  Guid? LinkedShippingInvoiceId,
  Guid? LinkedListingId
);

public sealed record CreateSupportTicketResponse(
  Guid TicketId,
  string TicketNumber,
  string? GuestAccessToken // only returned for guests
);

public sealed record CreateSupportMessageRequest(string Message);

public sealed record AdminSupportTicketListItem(
  Guid Id,
  string TicketNumber,
  string Subject,
  string Category,
  string Priority,
  string Status,
  Guid? AssignedToUserId,
  Guid? CreatedByUserId,
  string? GuestEmail,
  DateTimeOffset UpdatedAt
);

public sealed record AdminUpdateSupportTicketRequest(
  string? Status,
  string? Priority,
  Guid? AssignedToUserId
);

public sealed record AdminCreateSupportMessageRequest(
  string Message,
  bool IsInternalNote = false
);