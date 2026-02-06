using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class CheckoutPaymentConfiguration : IEntityTypeConfiguration<CheckoutPayment>
{
  public void Configure(EntityTypeBuilder<CheckoutPayment> b)
  {
    b.ToTable("checkout_payments");
    b.HasKey(x => x.Id);

    b.Property(x => x.Provider).IsRequired().HasMaxLength(20);
    b.Property(x => x.Status).IsRequired().HasMaxLength(30);

    b.Property(x => x.ProviderCheckoutId).HasMaxLength(200);
    b.Property(x => x.ProviderPaymentId).HasMaxLength(200);

    b.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(10);

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();

    b.HasIndex(x => x.HoldId);
    b.HasIndex(x => x.CartId);
    b.HasIndex(x => new { x.Provider, x.ProviderCheckoutId });

    b.HasOne(x => x.Hold)
      .WithMany()
      .HasForeignKey(x => x.HoldId)
      .OnDelete(DeleteBehavior.Restrict);

    b.HasOne(x => x.Cart)
      .WithMany()
      .HasForeignKey(x => x.CartId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
