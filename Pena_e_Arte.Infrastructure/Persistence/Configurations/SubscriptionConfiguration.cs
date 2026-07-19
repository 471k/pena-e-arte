using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.Id).HasName("pk_subscriptions");

        builder.Property(s => s.Status)
               .HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.StripeSubscriptionId).HasMaxLength(255);

        builder.Property(s => s.BillingInterval)
               .HasConversion<string>().HasMaxLength(32).IsRequired().HasDefaultValue(Domain.Enums.BillingInterval.Monthly);
        builder.Property(s => s.PendingBillingInterval)
               .HasConversion<string>().HasMaxLength(32);

        builder.HasOne(s => s.Studio)
               .WithOne(st => st.Subscription)
               .HasForeignKey<Subscription>(s => s.StudioId)
               .HasConstraintName("fk_subscriptions_studios")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Plan)
               .WithMany(p => p.Subscriptions)
               .HasForeignKey(s => s.PlanId)
               .HasConstraintName("fk_subscriptions_plans")
               .IsRequired(false)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Plan>()
               .WithMany()
               .HasForeignKey(s => s.PendingPlanId)
               .HasConstraintName("fk_subscriptions_pending_plans")
               .IsRequired(false)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.StudioId).IsUnique().HasDatabaseName("ix_subscriptions_studio_id");
        builder.HasIndex(s => s.StripeSubscriptionId).HasDatabaseName("ix_subscriptions_stripe_id");
    }
}
