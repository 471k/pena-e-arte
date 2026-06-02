using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ConsentFormConfiguration : TenantEntityConfiguration<ConsentForm>
{
    protected override string TableName => "consent_forms";

    public override void Configure(EntityTypeBuilder<ConsentForm> builder)
    {
        base.Configure(builder);

        builder.Property(f => f.FileUrl).HasMaxLength(1000);
        builder.Property(f => f.SignatureData).HasMaxLength(5000);

        builder.HasOne(f => f.Client)
               .WithMany()
               .HasForeignKey(f => f.ClientId)
               .HasConstraintName("fk_consent_forms_clients")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Appointment)
               .WithMany()
               .HasForeignKey(f => f.AppointmentId)
               .HasConstraintName("fk_consent_forms_appointments")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
