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
    }
}
