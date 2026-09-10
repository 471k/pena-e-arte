using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioHoursConfiguration : IEntityTypeConfiguration<StudioHours>
{
    public void Configure(EntityTypeBuilder<StudioHours> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.DayOfWeek)
               .HasConversion<int>();

        // Each studio has at most one hours entry per day — mirrors
        // ArtistScheduleConfiguration's uix_artist_schedule_artist_day exactly.
        builder.HasIndex(h => new { h.StudioId, h.DayOfWeek })
               .IsUnique()
               .HasDatabaseName("uix_studio_hours_studio_day");

        builder.HasIndex(h => h.StudioId)
               .HasDatabaseName("ix_studio_hours_studio_id");
    }
}
