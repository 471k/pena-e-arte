using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ManualReminderConfiguration : TenantEntityConfiguration<ManualReminder>
{
    protected override string TableName => "manual_reminders";

    public override void Configure(EntityTypeBuilder<ManualReminder> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Message).HasMaxLength(320);
        builder.Property(m => m.JobId).HasMaxLength(100);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(m => new { m.StudioId, m.ScheduledFor })
               .HasDatabaseName("ix_manual_reminders_studio_scheduled_for");
        builder.HasIndex(m => m.AppointmentId)
               .HasDatabaseName("ix_manual_reminders_appointment_id");
        builder.HasIndex(m => m.ClientId)
               .HasDatabaseName("ix_manual_reminders_client_id");

        builder.HasOne(m => m.Artist)
               .WithMany()
               .HasForeignKey(m => m.ArtistId)
               .HasConstraintName("fk_manual_reminders_artists")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Appointment)
               .WithMany()
               .HasForeignKey(m => m.AppointmentId)
               .HasConstraintName("fk_manual_reminders_appointments")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Client)
               .WithMany()
               .HasForeignKey(m => m.ClientId)
               .HasConstraintName("fk_manual_reminders_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
