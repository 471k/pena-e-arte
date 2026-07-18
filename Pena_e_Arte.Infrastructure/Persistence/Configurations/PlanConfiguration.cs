using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => p.Id).HasName("pk_plans");

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.BillingInterval)
               .HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.PriceMonthly).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.PriceYearly).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.StripePriceIdMonthly).HasMaxLength(255);
        builder.Property(p => p.StripePriceIdYearly).HasMaxLength(255);

        builder.Property(p => p.AllowApiAccess).HasDefaultValue(false);
        builder.Property(p => p.PrioritySupport).HasDefaultValue(false);

        // Self-referencing link between a tier's Monthly/Yearly rows — see Plan.PairedPlanId.
        // No FK constraint: deleting a plan clears the sibling's pointer explicitly in
        // DeletePlanHandler rather than relying on cascade/set-null semantics.
        builder.HasIndex(p => p.PairedPlanId).HasDatabaseName("ix_plans_paired_plan_id");
    }
}
