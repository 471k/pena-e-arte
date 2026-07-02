using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class InstagramConnectionConfiguration : IEntityTypeConfiguration<InstagramConnection>
{
    public void Configure(EntityTypeBuilder<InstagramConnection> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.InstagramUserId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Username).HasMaxLength(64).IsRequired();
        builder.Property(c => c.EncryptedToken).HasColumnType("TEXT").IsRequired();

        // One connection per artist at most
        builder.HasIndex(c => c.ArtistId).IsUnique();

        builder.HasOne(c => c.Artist)
               .WithMany()
               .HasForeignKey(c => c.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
