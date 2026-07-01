using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ArtistTimeOffConfiguration : IEntityTypeConfiguration<ArtistTimeOff>
{
    public void Configure(EntityTypeBuilder<ArtistTimeOff> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Reason)
               .HasMaxLength(500);

        builder.HasOne(t => t.Artist)
               .WithMany(a => a.TimeOff)
               .HasForeignKey(t => t.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.ArtistId, t.StartDate, t.EndDate })
               .HasDatabaseName("ix_artist_time_off_artist_dates");
    }
}
