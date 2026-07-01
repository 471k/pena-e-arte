using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : TenantEntityConfiguration<Appointment>
{
    protected override string TableName => "appointments";

    public override void Configure(EntityTypeBuilder<Appointment> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.Status)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(a => a.DepositStatus)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(a => a.DepositAmount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.Property(a => a.CancellationReason)
               .HasConversion<string>().HasMaxLength(32).IsRequired(false);

        builder.Property(a => a.ReminderJobId48h).HasMaxLength(128).IsRequired(false);
        builder.Property(a => a.ReminderJobId24h).HasMaxLength(128).IsRequired(false);

        builder.Property(a => a.AftercareSentAt).IsRequired(false);

        // Composite index for the booking overlap check query
        builder.HasIndex(a => new { a.ArtistId, a.Date, a.EndDate, a.Status })
               .HasDatabaseName("ix_appointments_artist_date_enddate_status");

        builder.HasIndex(a => new { a.StudioId, a.ArtistId, a.Date })
               .HasDatabaseName("ix_appointments_studio_artist_date");

        builder.HasOne(a => a.Artist)
               .WithMany(ar => ar.Appointments)
               .HasForeignKey(a => a.ArtistId)
               .HasConstraintName("fk_appointments_artists")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Client)
               .WithMany(c => c.Appointments)
               .HasForeignKey(a => a.ClientId)
               .HasConstraintName("fk_appointments_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
