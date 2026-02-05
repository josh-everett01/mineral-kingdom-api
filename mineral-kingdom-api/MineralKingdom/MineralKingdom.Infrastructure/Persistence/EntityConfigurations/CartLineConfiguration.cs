using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
  public void Configure(EntityTypeBuilder<CartLine> b)
  {
    b.ToTable("cart_lines");
    b.HasKey(x => x.Id);

    b.Property(x => x.Quantity).IsRequired();

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();

    // A cart should have at most one line per offer
    b.HasIndex(x => new { x.CartId, x.OfferId })
      .IsUnique()
      .HasDatabaseName("UX_cart_lines_CartId_OfferId");
  }
}
