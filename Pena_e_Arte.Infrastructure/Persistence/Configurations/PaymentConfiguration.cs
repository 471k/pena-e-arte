using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : TenantEntityConfiguration<Payment>
{
    protected override string TableName => "payments";

    public override void Configure(EntityTypeBuilder<Payment> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(p => p.Status)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);
        builder.Property(p => p.ClientSecret).HasMaxLength(500);

        builder.HasOne(p => p.Appointment)
               .WithMany()
               .HasForeignKey(p => p.AppointmentId)
               .HasConstraintName("fk_payments_appointments")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Client)
               .WithMany()
               .HasForeignKey(p => p.ClientId)
               .HasConstraintName("fk_payments_clients")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.SessionSplits)
               .WithOne(ss => ss.Payment)
               .HasForeignKey(ss => ss.PaymentId)
               .HasConstraintName("fk_session_splits_payments")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
