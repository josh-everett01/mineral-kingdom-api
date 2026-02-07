using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class CheckoutHoldItemConfiguration : IEntityTypeConfiguration<CheckoutHoldItem>
{
  public void Configure(EntityTypeBuilder<CheckoutHoldItem> b)
  {
    b.ToTable("checkout_hold_items");
    b.HasKey(x => x.Id);

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.IsActive).IsRequired();

    b.HasIndex(x => x.HoldId);
    b.HasIndex(x => x.ListingId);
    b.HasIndex(x => x.OfferId);

    // 🔒 Hard-hold exclusivity (qty=1 world):
    // Only one ACTIVE hold per ListingId at a time.
    // NOTE: Column names are quoted because EF creates "IsActive"/"ListingId" as-is in migrations.
    b.HasIndex(x => x.ListingId)
      .IsUnique()
      .HasFilter("\"IsActive\" = true");

    b.HasOne(x => x.Hold)
      .WithMany()
      .HasForeignKey(x => x.HoldId)
      .OnDelete(DeleteBehavior.Cascade);

    b.HasOne(x => x.Listing)
      .WithMany()
      .HasForeignKey(x => x.ListingId)
      .OnDelete(DeleteBehavior.Restrict);

    b.HasOne(x => x.Offer)
      .WithMany()
      .HasForeignKey(x => x.OfferId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
