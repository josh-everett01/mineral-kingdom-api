using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
  public void Configure(EntityTypeBuilder<OrderPayment> b)
  {
    b.ToTable("order_payments");
    b.HasKey(x => x.Id);

    b.Property(x => x.Provider).IsRequired().HasMaxLength(20);
    b.Property(x => x.Status).IsRequired().HasMaxLength(30);

    b.Property(x => x.ProviderCheckoutId).HasMaxLength(200);
    b.Property(x => x.ProviderPaymentId).HasMaxLength(200);

    b.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(10);

    b.Property(x => x.CreatedAt).IsRequired();
    b.Property(x => x.UpdatedAt).IsRequired();

    b.HasIndex(x => x.OrderId);
    b.HasIndex(x => new { x.Provider, x.ProviderCheckoutId });

    b.HasOne(x => x.Order)
      .WithMany()
      .HasForeignKey(x => x.OrderId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
