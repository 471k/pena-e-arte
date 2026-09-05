using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class AppointmentAttachmentConfiguration : TenantEntityConfiguration<AppointmentAttachment>
{
    protected override string TableName => "AppointmentAttachments";

    public override void Configure(EntityTypeBuilder<AppointmentAttachment> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(a => a.UploadedAt).IsRequired();
        builder.Property(a => a.Category)
               .HasConversion<string>()
               .HasMaxLength(16)
               .HasDefaultValue(Domain.Enums.AppointmentAttachmentCategory.Reference);

        builder.HasIndex(a => a.AppointmentId)
               .HasDatabaseName("ix_appointment_attachments_appointment_id");

        builder.HasOne(a => a.Appointment)
               .WithMany(a => a.Attachments)
               .HasForeignKey(a => a.AppointmentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
