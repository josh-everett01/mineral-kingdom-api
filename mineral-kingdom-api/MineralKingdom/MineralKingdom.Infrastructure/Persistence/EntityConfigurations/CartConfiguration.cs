using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
  public void Configure(EntityTypeBuilder<Cart> b)
  {
    b.ToTable("carts");
    b.HasKey(x => x.Id);

    b.Property(x => x.Status).IsRequired().HasMaxLength(30);

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();

    b.HasMany(x => x.Lines)
      .WithOne(x => x.Cart!)
      .HasForeignKey(x => x.CartId)
      .OnDelete(DeleteBehavior.Cascade);

    // Only one ACTIVE cart per user (member carts)
    b.HasIndex(x => x.UserId)
      .HasDatabaseName("IX_carts_UserId_Active")
      .HasFilter("\"UserId\" IS NOT NULL AND \"Status\" = 'ACTIVE'");
  }
}
