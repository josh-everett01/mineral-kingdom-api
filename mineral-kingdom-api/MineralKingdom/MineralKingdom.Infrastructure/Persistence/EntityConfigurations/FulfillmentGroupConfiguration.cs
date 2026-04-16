using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Persistence.Configurations;

public sealed class FulfillmentGroupConfiguration : IEntityTypeConfiguration<FulfillmentGroup>
{
  public void Configure(EntityTypeBuilder<FulfillmentGroup> builder)
  {
    builder.Property(x => x.ShipmentRequestStatus)
      .HasMaxLength(32)
      .IsRequired();

    builder.Property(x => x.ShipmentRequestedAt);
    builder.Property(x => x.ShipmentReviewedAt);
    builder.Property(x => x.ShipmentReviewedByUserId);
  }
}