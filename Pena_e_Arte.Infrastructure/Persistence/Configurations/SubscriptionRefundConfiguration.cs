using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SubscriptionRefundConfiguration : IEntityTypeConfiguration<SubscriptionRefund>
{
    public void Configure(EntityTypeBuilder<SubscriptionRefund> builder)
    {
        builder.ToTable("subscription_refunds");
        builder.HasKey(r => r.Id).HasName("pk_subscription_refunds");

        builder.Property(r => r.StripeRefundId).HasMaxLength(255);
        builder.Property(r => r.StripeInvoiceId).HasMaxLength(255).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(10, 2);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.AmountPaid).HasPrecision(10, 2);
        builder.Property(r => r.MonthlyReferencePrice).HasPrecision(10, 2);
        builder.Property(r => r.Rule).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.AdminReason).HasMaxLength(2000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.FailureReason).HasMaxLength(2000);

        // One refund per invoice — the DB-level half of A3's "exactly one Stripe refund" idempotency.
        builder.HasIndex(r => r.SubscriptionInvoicePaymentId).IsUnique()
               .HasDatabaseName("ix_subscription_refunds_subscription_invoice_payment_id");
        builder.HasIndex(r => r.StudioId).HasDatabaseName("ix_subscription_refunds_studio_id");

        builder.HasOne(r => r.Subscription).WithMany().HasForeignKey(r => r.SubscriptionId)
               .HasConstraintName("fk_subscription_refunds_subscriptions").OnDelete(DeleteBehavior.Cascade);
    }
}
