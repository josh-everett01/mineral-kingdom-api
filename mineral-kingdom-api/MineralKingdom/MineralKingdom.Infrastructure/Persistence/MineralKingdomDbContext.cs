using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Persistence.Entities.Cms;

namespace MineralKingdom.Infrastructure.Persistence;

public class MineralKingdomDbContext : DbContext
{
    public MineralKingdomDbContext(DbContextOptions<MineralKingdomDbContext> options)
    : base(options)
    {
    }
    public DbSet<DbPing> Pings => Set<DbPing>();

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<BackgroundJob> Jobs => Set<BackgroundJob>();
    public DbSet<Mineral> Minerals => Set<Mineral>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingMedia> ListingMedia => Set<ListingMedia>();
    public DbSet<StoreOffer> StoreOffers => Set<StoreOffer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<CheckoutHold> CheckoutHolds => Set<CheckoutHold>();
    public DbSet<CheckoutPayment> CheckoutPayments => Set<CheckoutPayment>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<CheckoutHoldItem> CheckoutHoldItems => Set<CheckoutHoldItem>();
    public DbSet<OrderLedgerEntry> OrderLedgerEntries => Set<OrderLedgerEntry>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionMaxBid> AuctionMaxBids => Set<AuctionMaxBid>();
    public DbSet<AuctionBidEvent> AuctionBidEvents => Set<AuctionBidEvent>();
    public DbSet<FulfillmentGroup> FulfillmentGroups => Set<FulfillmentGroup>();
    public DbSet<ShippingInvoice> ShippingInvoices => Set<ShippingInvoice>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<UserNotificationPreferences> UserNotificationPreferences => Set<UserNotificationPreferences>();
    public DbSet<OrderRefund> OrderRefunds => Set<OrderRefund>();
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
    public DbSet<SupportTicketAccessToken> SupportTicketAccessTokens => Set<SupportTicketAccessToken>();
    public DbSet<CmsPage> CmsPages => Set<CmsPage>();
    public DbSet<CmsPageRevision> CmsPageRevisions => Set<CmsPageRevision>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MineralKingdomDbContext).Assembly);

        modelBuilder.Entity<DbPing>(b =>
        {
            b.ToTable("db_pings");
            b.HasKey(x => x.Id);
            b.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email).IsUnique();

            b.Property(x => x.Email).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.EmailVerified).HasDefaultValue(false);
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(UserRoles.User);

        });

        modelBuilder.Entity<EmailVerificationToken>(b =>
        {
            b.ToTable("email_verification_tokens");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TokenHash).IsUnique();

            b.Property(x => x.TokenHash).IsRequired();
            b.Property(x => x.ExpiresAt).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();

            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(x => x.Id);

            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.UserId);

            b.Property(x => x.TokenHash).IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.ExpiresAt).IsRequired();

            b.Property(x => x.UsedAt);
            b.Property(x => x.RevokedAt);

            b.Property(x => x.ReplacedByTokenHash);

            b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminAuditLog>(e =>
        {
            e.ToTable("admin_audit_logs");
            e.HasKey(x => x.Id);

            e.Property(x => x.ActionType).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            e.Property(x => x.ActorRole).HasMaxLength(20);

            e.Property(x => x.IpAddress).HasMaxLength(64);

            e.Property(x => x.BeforeJson).HasColumnType("jsonb");
            e.Property(x => x.AfterJson).HasColumnType("jsonb");

            e.HasIndex(x => x.ActorUserId);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.ToTable("password_reset_tokens");
            b.HasKey(x => x.Id);

            b.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.UserId);

            b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupportTicket>(b =>
{
    b.ToTable("support_tickets");
    b.HasKey(x => x.Id);

    b.Property(x => x.TicketNumber).IsRequired().HasMaxLength(32);
    b.HasIndex(x => x.TicketNumber).IsUnique();

    b.Property(x => x.GuestEmail).HasMaxLength(320);

    b.Property(x => x.Subject).IsRequired().HasMaxLength(200);
    b.Property(x => x.Category).IsRequired().HasMaxLength(30);
    b.Property(x => x.Priority).IsRequired().HasMaxLength(10).HasDefaultValue("NORMAL");
    b.Property(x => x.Status).IsRequired().HasMaxLength(30).HasDefaultValue("OPEN");

    b.HasIndex(x => new { x.Status, x.Priority, x.UpdatedAt });
    b.HasIndex(x => x.GuestEmail);
    b.HasIndex(x => x.CreatedByUserId);
    b.HasIndex(x => x.AssignedToUserId);

    b.HasOne(x => x.CreatedByUser)
      .WithMany()
      .HasForeignKey(x => x.CreatedByUserId)
      .OnDelete(DeleteBehavior.SetNull);

    b.HasOne(x => x.AssignedToUser)
      .WithMany()
      .HasForeignKey(x => x.AssignedToUserId)
      .OnDelete(DeleteBehavior.SetNull);

    b.HasMany(x => x.Messages)
      .WithOne(m => m.Ticket)
      .HasForeignKey(m => m.TicketId)
      .OnDelete(DeleteBehavior.Cascade);

    b.HasMany(x => x.AccessTokens)
      .WithOne(t => t.Ticket)
      .HasForeignKey(t => t.TicketId)
      .OnDelete(DeleteBehavior.Cascade);
});

        modelBuilder.Entity<SupportTicketMessage>(b =>
        {
            b.ToTable("support_ticket_messages");
            b.HasKey(x => x.Id);

            b.Property(x => x.AuthorType).IsRequired().HasMaxLength(20);
            b.Property(x => x.BodyText).IsRequired().HasMaxLength(4000);
            b.Property(x => x.IsInternalNote).HasDefaultValue(false);

            b.HasIndex(x => new { x.TicketId, x.CreatedAt });

            b.HasOne(x => x.AuthorUser)
      .WithMany()
      .HasForeignKey(x => x.AuthorUserId)
      .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SupportTicketAccessToken>(b =>
        {
            b.ToTable("support_ticket_access_tokens");
            b.HasKey(x => x.Id);

            b.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.TicketId);
        });

        modelBuilder.Entity<BackgroundJob>(b =>
        {
            b.ToTable("jobs");

            b.HasKey(x => x.Id);

            b.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(80);

            b.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("PENDING");

            b.Property(x => x.PayloadJson)
            .HasColumnType("jsonb");

            b.Property(x => x.Attempts)
            .HasDefaultValue(0);

            b.Property(x => x.MaxAttempts)
            .HasDefaultValue(8);

            b.Property(x => x.RunAt)
            .IsRequired();

            b.Property(x => x.LockedBy)
            .HasMaxLength(80);

            b.Property(x => x.LastError)
            .HasColumnType("text");

            b.HasIndex(x => new { x.Status, x.RunAt })
            .HasDatabaseName("IX_jobs_Status_RunAt");

            b.HasIndex(x => x.LockedAt)
            .HasDatabaseName("IX_jobs_LockedAt");
        });

        modelBuilder.Entity<Mineral>(b =>
        {
            b.ToTable("minerals");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.HasIndex(x => x.Name).IsUnique();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<Listing>(b =>
        {
            b.ToTable("listings");
            b.HasKey(x => x.Id);

            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Description).HasColumnType("text");

            b.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(30)
                .HasDefaultValue("DRAFT");

            b.Property(x => x.LocalityDisplay).HasMaxLength(400);
            b.Property(x => x.CountryCode).HasMaxLength(2);
            b.Property(x => x.AdminArea1).HasMaxLength(120);
            b.Property(x => x.AdminArea2).HasMaxLength(120);
            b.Property(x => x.MineName).HasMaxLength(200);

            b.Property(x => x.LengthCm).HasColumnType("numeric(6,2)");
            b.Property(x => x.WidthCm).HasColumnType("numeric(6,2)");
            b.Property(x => x.HeightCm).HasColumnType("numeric(6,2)");

            b.Property(x => x.WeightGrams);

            b.Property(x => x.SizeClass).HasMaxLength(30);
            b.Property(x => x.IsFluorescent).HasDefaultValue(false);
            b.Property(x => x.FluorescenceNotes).HasColumnType("text");
            b.Property(x => x.ConditionNotes).HasColumnType("text");

            b.Property(x => x.IsLot).HasDefaultValue(false);
            b.Property(x => x.QuantityTotal).HasDefaultValue(1);
            b.Property(x => x.QuantityAvailable).HasDefaultValue(1);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.PublishedAt);
            b.Property(x => x.ArchivedAt);

            b.HasOne(x => x.PrimaryMineral)
                .WithMany()
                .HasForeignKey(x => x.PrimaryMineralId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Media)
                .WithOne(x => x.Listing)
                .HasForeignKey(x => x.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.Status, x.PublishedAt })
                .HasDatabaseName("IX_listings_Status_PublishedAt");

            b.HasIndex(x => x.PrimaryMineralId)
                .HasDatabaseName("IX_listings_PrimaryMineralId");

            b.HasIndex(x => new { x.CountryCode, x.AdminArea1 })
                .HasDatabaseName("IX_listings_Country_AdminArea1");

            b.HasIndex(x => x.SizeClass)
                .HasDatabaseName("IX_listings_SizeClass");
        });

        modelBuilder.Entity<ListingMedia>(b =>
        {
            b.ToTable("listing_media");
            b.HasKey(x => x.Id);

            b.Property(x => x.MediaType).IsRequired().HasMaxLength(10);

            b.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(MineralKingdom.Contracts.Listings.ListingMediaStatuses.Ready);

            b.Property(x => x.StorageKey).HasMaxLength(512);
            b.Property(x => x.OriginalFileName).HasMaxLength(255);
            b.Property(x => x.ContentType).HasMaxLength(255);

            b.Property(x => x.ContentLengthBytes)
            .IsRequired()
            .HasDefaultValue(0L);

            b.Property(x => x.Url).IsRequired().HasMaxLength(2000);

            b.Property(x => x.SortOrder).HasDefaultValue(0);
            b.Property(x => x.IsPrimary).HasDefaultValue(false);
            b.Property(x => x.Caption).HasMaxLength(500);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.DeletedAt);

            b.HasIndex(x => x.ListingId)
            .HasDatabaseName("IX_listing_media_ListingId");

            b.HasIndex(x => new { x.ListingId, x.SortOrder })
            .HasDatabaseName("IX_listing_media_ListingId_SortOrder");

            // Optional but useful for immutability/debugging (you can add later if desired):
            // b.HasIndex(x => x.StorageKey)
            //   .HasDatabaseName("IX_listing_media_StorageKey");
        });
        modelBuilder.Entity<Auction>(b =>
        {
            b.ToTable("auctions");
            b.HasKey(x => x.Id);

            b.Property(x => x.Status).IsRequired().HasMaxLength(30).HasDefaultValue("DRAFT");

            b.Property(x => x.StartingPriceCents).IsRequired();
            b.Property(x => x.ReservePriceCents);
            b.Property(x => x.StartTime);
            b.Property(x => x.CloseTime).IsRequired();
            b.Property(x => x.ClosingWindowEnd);

            b.Property(x => x.CurrentPriceCents).IsRequired();
            b.Property(x => x.CurrentLeaderUserId);
            b.Property(x => x.CurrentLeaderMaxCents);
            b.Property(x => x.BidCount).IsRequired().HasDefaultValue(0);
            b.Property(x => x.ReserveMet).IsRequired().HasDefaultValue(false);
            b.Property(x => x.RelistOfAuctionId);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasIndex(x => x.ListingId).HasDatabaseName("IX_auctions_ListingId");
            b.HasIndex(x => new { x.Status, x.CloseTime }).HasDatabaseName("IX_auctions_Status_CloseTime");
            b.HasIndex(x => new { x.Status, x.UpdatedAt })
            .HasDatabaseName("IX_auctions_Status_UpdatedAt");
            b.HasIndex(x => x.RelistOfAuctionId)
            .IsUnique()
            .HasDatabaseName("UX_auctions_RelistOfAuctionId");
        });

        modelBuilder.Entity<AuctionMaxBid>(b =>
        {
            b.ToTable("auction_max_bids");
            b.HasKey(x => new { x.AuctionId, x.UserId });

            b.Property(x => x.BidType).IsRequired().HasMaxLength(20);
            b.Property(x => x.MaxBidCents).IsRequired();
            b.Property(x => x.ReceivedAt).IsRequired();

            b.HasOne(x => x.Auction)
                .WithMany()
                .HasForeignKey(x => x.AuctionId);

            b.HasIndex(x => new { x.AuctionId, x.MaxBidCents, x.ReceivedAt })
                .HasDatabaseName("IX_auction_max_bids_Auction_Max_Received");
        });

        modelBuilder.Entity<AuctionBidEvent>(b =>
        {
            b.ToTable("auction_bid_events");
            b.HasKey(x => x.Id);

            b.Property(x => x.EventType).IsRequired().HasMaxLength(50);
            b.Property(x => x.DataJson);
            b.Property(x => x.ServerReceivedAt).IsRequired();

            b.HasOne(x => x.Auction)
                .WithMany()
                .HasForeignKey(x => x.AuctionId);

            b.HasIndex(x => new { x.AuctionId, x.ServerReceivedAt })
                .HasDatabaseName("IX_auction_bid_events_Auction_Time");
        });

        modelBuilder.Entity<StoreOffer>(b =>
        {
            b.ToTable("store_offers");
            b.HasKey(x => x.Id);

            b.Property(x => x.DiscountType).IsRequired().HasMaxLength(20);

            b.Property(x => x.PriceCents).IsRequired();
            b.Property(x => x.DiscountCents);
            b.Property(x => x.DiscountPercentBps);

            b.Property(x => x.IsActive).HasDefaultValue(true);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.DeletedAt);

            b.HasIndex(x => x.ListingId).HasDatabaseName("IX_store_offers_ListingId");
            b.HasIndex(x => new { x.ListingId, x.IsActive }).HasDatabaseName("IX_store_offers_ListingId_IsActive");
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);

            b.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
            b.Property(x => x.Status).IsRequired().HasMaxLength(20);

            // S4-4 additions
            b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(32);
            b.Property(x => x.GuestEmail).HasMaxLength(320);
            b.Property(x => x.CheckoutHoldId);
            b.Property(x => x.PaidAt);

            b.Property(x => x.SubtotalCents).IsRequired();
            b.Property(x => x.DiscountTotalCents).IsRequired();
            b.Property(x => x.TotalCents).IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasMany(x => x.Lines)
            .WithOne(x => x.Order!)
            .HasForeignKey(x => x.OrderId);

            // Indexes
            b.HasIndex(x => x.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_orders_OrderNumber");

            b.HasIndex(x => x.CheckoutHoldId)
            .IsUnique()
            .HasFilter("\"CheckoutHoldId\" IS NOT NULL")
            .HasDatabaseName("UX_orders_CheckoutHoldId");

            b.HasIndex(x => x.UserId).HasDatabaseName("IX_orders_UserId");
            b.HasIndex(x => x.GuestEmail).HasDatabaseName("IX_orders_GuestEmail");

            // S5-4 additions
            b.Property(x => x.SourceType).IsRequired().HasMaxLength(20).HasDefaultValue("STORE");
            b.Property(x => x.AuctionId);
            b.Property(x => x.PaymentDueAt);

            b.HasIndex(x => x.AuctionId)
              .IsUnique()
              .HasFilter("\"AuctionId\" IS NOT NULL")
              .HasDatabaseName("UX_orders_AuctionId");

        });

        modelBuilder.Entity<OrderLine>(b =>
        {
            b.ToTable("order_lines");
            b.HasKey(x => x.Id);

            b.Property(x => x.Quantity).IsRequired();

            b.Property(x => x.UnitPriceCents).IsRequired();
            b.Property(x => x.UnitDiscountCents).IsRequired();
            b.Property(x => x.UnitFinalPriceCents).IsRequired();

            b.Property(x => x.LineSubtotalCents).IsRequired();
            b.Property(x => x.LineDiscountCents).IsRequired();
            b.Property(x => x.LineTotalCents).IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasIndex(x => x.OrderId).HasDatabaseName("IX_order_lines_OrderId");
            b.HasIndex(x => x.OfferId).HasDatabaseName("IX_order_lines_OfferId");
            b.HasIndex(x => x.ListingId).HasDatabaseName("IX_order_lines_ListingId");
        });

        modelBuilder.Entity<OrderLedgerEntry>(b =>
        {
            b.ToTable("order_ledger_entries");
            b.HasKey(x => x.Id);

            b.Property(x => x.EventType).IsRequired().HasMaxLength(50);
            b.Property(x => x.DataJson);
            b.Property(x => x.CreatedAt).IsRequired();

            b.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId);

            b.HasIndex(x => x.OrderId).HasDatabaseName("IX_order_ledger_entries_OrderId");
        });

        modelBuilder.Entity<FulfillmentGroup>(b =>
        {
            b.ToTable("fulfillment_groups");
            b.HasKey(x => x.Id);

            b.Property(x => x.GuestEmail).HasMaxLength(320);
            b.Property(x => x.Status).HasMaxLength(32).IsRequired();

            b.Property(x => x.ShippingCarrier).HasMaxLength(64);
            b.Property(x => x.TrackingNumber).HasMaxLength(128);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.UpdatedAt);

            // Orders link (FK lives on Order)
            b.HasMany(x => x.Orders)
            .WithOne(x => x.FulfillmentGroup)
            .HasForeignKey(x => x.FulfillmentGroupId)
            .OnDelete(DeleteBehavior.SetNull);

            b.Property(x => x.BoxStatus).HasMaxLength(16).IsRequired().HasDefaultValue("CLOSED");
            b.Property(x => x.ClosedAt);

            b.HasIndex(x => x.BoxStatus);
            // optional but useful for admin queues
            b.HasIndex(x => new { x.BoxStatus, x.UpdatedAt });

            // Shipping invoices
            b.HasMany(x => x.ShippingInvoices)
            .WithOne(x => x.FulfillmentGroup)
            .HasForeignKey(x => x.FulfillmentGroupId)
            .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShippingInvoice>(b =>
        {
            b.ToTable("shipping_invoices");
            b.HasKey(x => x.Id);

            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasIndex(x => new { x.FulfillmentGroupId, x.Status });

            b.Property(x => x.Provider).HasMaxLength(20);
            b.Property(x => x.ProviderCheckoutId).HasMaxLength(200);
            b.Property(x => x.ProviderPaymentId).HasMaxLength(200);
            b.Property(x => x.PaymentReference).HasMaxLength(200);

            b.Property(x => x.IsOverride).HasDefaultValue(false);
            b.Property(x => x.OverrideReason).HasMaxLength(500);

            // Helpful for webhook lookups / idempotency
            b.HasIndex(x => new { x.Provider, x.ProviderCheckoutId });
            b.Property(x => x.CalculatedAmountCents).HasDefaultValue(0L);
        });

        modelBuilder.Entity<EmailOutbox>(b =>
        {
            b.ToTable("email_outbox");

            b.HasKey(x => x.Id);

            b.Property(x => x.ToEmail).HasMaxLength(320).IsRequired();
            b.Property(x => x.TemplateKey).HasMaxLength(80).IsRequired();

            b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();

            b.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.DedupeKey).IsUnique().HasDatabaseName("UX_email_outbox_DedupeKey");

            b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");

            b.Property(x => x.Attempts).HasDefaultValue(0);
            b.Property(x => x.MaxAttempts).HasDefaultValue(8);

            b.Property(x => x.LastError).HasColumnType("text");

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.SentAt);

            b.HasIndex(x => new { x.Status, x.UpdatedAt }).HasDatabaseName("IX_email_outbox_Status_UpdatedAt");
        });

        modelBuilder.Entity<UserNotificationPreferences>(b =>
        {
            b.ToTable("user_notification_preferences");

            b.HasKey(x => x.UserId);

            b.Property(x => x.OutbidEmailEnabled).HasDefaultValue(true);
            b.Property(x => x.AuctionPaymentRemindersEnabled).HasDefaultValue(true);
            b.Property(x => x.ShippingInvoiceRemindersEnabled).HasDefaultValue(true);
            b.Property(x => x.BidAcceptedEmailEnabled).HasDefaultValue(false);

            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserNotificationPreferences>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OrderRefund>(b =>
        {
            b.ToTable("order_refunds");

            b.HasKey(x => x.Id);

            b.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            b.Property(x => x.ProviderRefundId).HasMaxLength(200);

            b.Property(x => x.AmountCents).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();

            b.Property(x => x.Reason).HasMaxLength(500);

            b.Property(x => x.CreatedAt).IsRequired();

            b.HasIndex(x => new { x.OrderId, x.CreatedAt }).HasDatabaseName("IX_order_refunds_OrderId_CreatedAt");
            b.HasIndex(x => new { x.Provider, x.ProviderRefundId }).HasDatabaseName("IX_order_refunds_Provider_ProviderRefundId");

            b.HasOne(x => x.Order)
            .WithMany(o => o.Refunds)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CmsPage>(b =>
{
    b.ToTable("cms_pages");
    b.HasKey(x => x.Id);

    b.Property(x => x.Slug).IsRequired().HasMaxLength(64);
    b.HasIndex(x => x.Slug).IsUnique();

    b.Property(x => x.Title).IsRequired().HasMaxLength(200);
    b.Property(x => x.Category).IsRequired().HasMaxLength(20);
    b.Property(x => x.IsActive).HasDefaultValue(true);

    b.HasMany(x => x.Revisions)
      .WithOne(r => r.Page)
      .HasForeignKey(r => r.PageId)
      .OnDelete(DeleteBehavior.Cascade);

    b.HasIndex(x => new { x.Category, x.IsActive });
});

        modelBuilder.Entity<CmsPageRevision>(b =>
        {
            b.ToTable("cms_page_revisions");
            b.HasKey(x => x.Id);

            b.Property(x => x.Status).IsRequired().HasMaxLength(20);
            b.Property(x => x.ContentMarkdown).IsRequired().HasMaxLength(20000);
            b.Property(x => x.ContentHtml).HasMaxLength(40000);
            b.Property(x => x.ChangeSummary).HasMaxLength(500);

            b.HasIndex(x => new { x.PageId, x.Status });

            // Postgres partial unique index: only one published revision per page.
            b.HasIndex(x => x.PageId)
      .IsUnique()
      .HasFilter("\"Status\" = 'PUBLISHED'");

            // we don’t FK to users everywhere in this project; store IDs.
        });
    }
}
