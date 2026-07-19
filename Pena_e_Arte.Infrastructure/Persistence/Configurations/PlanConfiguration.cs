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
        builder.Property(p => p.AllowApiAccess).HasDefaultValue(false);
        builder.Property(p => p.PrioritySupport).HasDefaultValue(false);

        builder.HasMany(p => p.Prices)
               .WithOne(pp => pp.Plan)
               .HasForeignKey(pp => pp.PlanId)
               .HasConstraintName("fk_plan_prices_plans")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
