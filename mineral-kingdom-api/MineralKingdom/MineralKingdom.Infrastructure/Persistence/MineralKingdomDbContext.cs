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





    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
    }
}
