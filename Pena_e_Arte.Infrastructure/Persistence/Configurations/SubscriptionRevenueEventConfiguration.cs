using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SubscriptionRevenueEventConfiguration : IEntityTypeConfiguration<SubscriptionRevenueEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionRevenueEvent> builder)
    {
        builder.ToTable("subscription_revenue_events");
        builder.HasKey(e => e.Id).HasName("pk_subscription_revenue_events");

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Interval).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.MrrBefore).HasPrecision(10, 2);
        builder.Property(e => e.MrrAfter).HasPrecision(10, 2);
        builder.Property(e => e.Source).HasMaxLength(100).IsRequired();
        builder.Property(e => e.StripeEventId).HasMaxLength(255);

        builder.HasIndex(e => e.SubscriptionId).HasDatabaseName("ix_subscription_revenue_events_subscription_id");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_subscription_revenue_events_occurred_at");

        // MySQL/EF Core don't support a filtered unique index the way SQL Server does, so this is
        // a plain unique index — every row needs a non-null StripeEventId. Non-webhook sources get
        // a deterministic synthetic key from RevenueEventRecorder so it still does useful work.
        builder.HasIndex(e => e.StripeEventId).IsUnique()
               .HasDatabaseName("ix_subscription_revenue_events_stripe_event_id");

        builder.HasOne<Subscription>().WithMany().HasForeignKey(e => e.SubscriptionId)
               .HasConstraintName("fk_subscription_revenue_events_subscriptions").OnDelete(DeleteBehavior.Cascade);
    }
}
