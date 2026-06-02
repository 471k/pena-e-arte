using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ArtistConfiguration : TenantEntityConfiguration<Artist>
{
    protected override string TableName => "artists";

    public override void Configure(EntityTypeBuilder<Artist> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.LastName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Specializations).HasMaxLength(1000);

        builder.HasIndex(a => new { a.StudioId, a.Email })
               .IsUnique()
               .HasDatabaseName("ix_artists_studio_email");
    }
}
