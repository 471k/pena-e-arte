using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PlanPriceConfiguration : IEntityTypeConfiguration<PlanPrice>
{
    public void Configure(EntityTypeBuilder<PlanPrice> builder)
    {
        builder.ToTable("plan_prices");
        builder.HasKey(pp => pp.Id).HasName("pk_plan_prices");

        builder.Property(pp => pp.Interval)
               .HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(pp => pp.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(pp => pp.StripePriceId).HasMaxLength(255);
        builder.Property(pp => pp.IsActive).HasDefaultValue(true);

        // One row per (tier, interval) — this is the invariant that makes an "orphan
        // duplicate" structurally impossible going forward.
        builder.HasIndex(pp => new { pp.PlanId, pp.Interval })
               .IsUnique()
               .HasDatabaseName("ux_plan_prices_plan_id_interval");
    }
}
