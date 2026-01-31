using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
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
    }
}
