using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ArtistScheduleConfiguration : IEntityTypeConfiguration<ArtistSchedule>
{
    public void Configure(EntityTypeBuilder<ArtistSchedule> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DayOfWeek)
               .HasConversion<int>();

        builder.HasOne(s => s.Artist)
               .WithMany(a => a.Schedule)
               .HasForeignKey(s => s.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);

        // Each artist has at most one schedule entry per day
        builder.HasIndex(s => new { s.ArtistId, s.DayOfWeek })
               .IsUnique()
               .HasDatabaseName("uix_artist_schedule_artist_day");

        builder.HasIndex(s => s.ArtistId)
               .HasDatabaseName("ix_artist_schedule_artist_id");
    }
}
