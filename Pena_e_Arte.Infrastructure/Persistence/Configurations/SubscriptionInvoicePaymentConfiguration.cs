using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SubscriptionInvoicePaymentConfiguration : IEntityTypeConfiguration<SubscriptionInvoicePayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoicePayment> builder)
    {
        builder.ToTable("subscription_invoice_payments");
        builder.HasKey(p => p.Id).HasName("pk_subscription_invoice_payments");

        builder.Property(p => p.StripeInvoiceId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.AmountPaid).HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.DiscountAmount).HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.MonthlyReferencePrice).HasPrecision(10, 2);
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);

        builder.HasIndex(p => p.StripeInvoiceId).IsUnique().HasDatabaseName("ix_subscription_invoice_payments_stripe_invoice_id");
        builder.HasIndex(p => p.StudioId).HasDatabaseName("ix_subscription_invoice_payments_studio_id");
        builder.HasIndex(p => p.PaidAt).HasDatabaseName("ix_subscription_invoice_payments_paid_at");

        builder.HasOne(p => p.Subscription)
               .WithMany()
               .HasForeignKey(p => p.SubscriptionId)
               .HasConstraintName("fk_subscription_invoice_payments_subscriptions")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
