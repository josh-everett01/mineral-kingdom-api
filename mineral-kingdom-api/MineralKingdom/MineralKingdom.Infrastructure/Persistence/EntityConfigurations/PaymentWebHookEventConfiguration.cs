using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.EntityConfigurations;

public sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
  public void Configure(EntityTypeBuilder<PaymentWebhookEvent> b)
  {
    b.ToTable("payment_webhook_events");
    b.HasKey(x => x.Id);

    b.Property(x => x.Provider).IsRequired().HasMaxLength(20);
    b.Property(x => x.EventId).IsRequired().HasMaxLength(120);

    b.Property(x => x.PayloadJson)
      .IsRequired()
      .HasColumnType("jsonb");

    b.Property(x => x.ReceivedAt).IsRequired();

    // Idempotency guarantee
    b.HasIndex(x => new { x.Provider, x.EventId })
      .HasDatabaseName("UX_payment_webhook_events_provider_event")
      .IsUnique();

    b.HasIndex(x => x.CheckoutPaymentId);
    b.HasIndex(x => x.OrderPaymentId);

    b.HasOne(x => x.CheckoutPayment)
      .WithMany()
      .HasForeignKey(x => x.CheckoutPaymentId)
      .OnDelete(DeleteBehavior.SetNull);

    b.HasOne(x => x.OrderPayment)
      .WithMany()
      .HasForeignKey(x => x.OrderPaymentId)
      .OnDelete(DeleteBehavior.SetNull);
  }
}
