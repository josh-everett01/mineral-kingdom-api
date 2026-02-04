using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence.Entities;

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
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<StoreOffer> StoreOffers => Set<StoreOffer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();



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

            b.Property(x => x.Email).IsRequired().HasMaxLength(320);
            b.Property(x => x.Subject).IsRequired().HasMaxLength(200);
            b.Property(x => x.Category).IsRequired().HasMaxLength(30);
            b.Property(x => x.Message).IsRequired().HasMaxLength(4000);
            b.Property(x => x.Status).IsRequired().HasMaxLength(30);

            b.HasIndex(x => x.CreatedAt);
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

            b.Property(x => x.Status).IsRequired().HasMaxLength(20);

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasIndex(x => x.ListingId).HasDatabaseName("IX_auctions_ListingId");
            b.HasIndex(x => new { x.ListingId, x.Status }).HasDatabaseName("IX_auctions_ListingId_Status");
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

            b.Property(x => x.SubtotalCents).IsRequired();
            b.Property(x => x.DiscountTotalCents).IsRequired();
            b.Property(x => x.TotalCents).IsRequired();

            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();

            b.HasMany(x => x.Lines)
                .WithOne(x => x.Order!)
                .HasForeignKey(x => x.OrderId);

            b.HasIndex(x => x.UserId).HasDatabaseName("IX_orders_UserId");
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

    }
}
