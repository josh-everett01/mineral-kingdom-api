using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class StoreOfferConfiguration : IEntityTypeConfiguration<StoreOffer>
{
  public void Configure(EntityTypeBuilder<StoreOffer> b)
  {
    b.ToTable("store_offers");

    b.HasKey(x => x.Id);

    b.Property(x => x.ListingId).IsRequired();

    // cents
    b.Property(x => x.PriceCents).IsRequired();

    // discount shape
    b.Property(x => x.DiscountType)
      .HasMaxLength(20)
      .IsRequired();

    b.Property(x => x.DiscountCents);
    b.Property(x => x.DiscountPercentBps);

    b.Property(x => x.IsActive).IsRequired();

    b.Property(x => x.StartsAt);
    b.Property(x => x.EndsAt);

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();
    b.Property(x => x.DeletedAt);

    // Helpful index: only one "active" offer per listing (soft-deletes excluded)
    b.HasIndex(x => new { x.ListingId, x.IsActive });

    // If you want to enforce one active offer per listing at DB level, we can do a partial unique index
    // in the migration (Postgres). We'll do that later if desired.
  }
}
