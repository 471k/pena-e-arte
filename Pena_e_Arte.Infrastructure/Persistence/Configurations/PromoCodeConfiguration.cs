using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PromoCodeConfiguration : TenantEntityConfiguration<PromoCode>
{
    protected override string TableName => "promo_codes";

    public override void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Code).IsRequired().HasMaxLength(40);
        builder.Property(p => p.AmountFixed).HasColumnType("decimal(18,2)");
        builder.Property(p => p.AmountPercent).HasColumnType("decimal(5,2)");
        builder.Property(p => p.RedemptionCount).HasDefaultValue(0);

        builder.HasIndex(p => new { p.StudioId, p.Code });
    }
}
