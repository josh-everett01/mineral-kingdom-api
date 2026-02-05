using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class CheckoutHoldConfiguration : IEntityTypeConfiguration<CheckoutHold>
{
  public void Configure(EntityTypeBuilder<CheckoutHold> b)
  {
    b.ToTable("checkout_holds");
    b.HasKey(x => x.Id);

    b.Property(x => x.Status).IsRequired().HasMaxLength(30);
    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();
    b.Property(x => x.ExpiresAt).IsRequired();

    b.Property(x => x.PaymentReference).HasMaxLength(200);

    // Enforce: only ONE completed hold per cart (first successful payment wins)
    b.HasIndex(x => x.CartId)
      .HasDatabaseName("IX_checkout_holds_CartId_Completed")
      .IsUnique()
      .HasFilter("\"Status\" = 'COMPLETED'");
  }
}
