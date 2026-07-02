using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class InstagramPostConfiguration : IEntityTypeConfiguration<InstagramPost>
{
    public void Configure(EntityTypeBuilder<InstagramPost> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.InstagramMediaId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.MediaUrl).HasMaxLength(2048).IsRequired();
        builder.Property(p => p.ThumbnailUrl).HasMaxLength(2048);
        builder.Property(p => p.Caption).HasMaxLength(2200);
        builder.Property(p => p.MediaType).HasMaxLength(32).IsRequired();

        // Idempotent upsert key
        builder.HasIndex(p => p.InstagramMediaId).IsUnique();

        builder.HasOne(p => p.Artist)
               .WithMany()
               .HasForeignKey(p => p.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);

        // Efficient public portfolio query: artist + visible only
        builder.HasIndex(p => new { p.ArtistId, p.IsVisible });
    }
}
