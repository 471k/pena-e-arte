using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class DesignConfiguration : TenantEntityConfiguration<Design>
{
    protected override string TableName => "designs";

    public override void Configure(EntityTypeBuilder<Design> builder)
    {
        base.Configure(builder);

        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);

        builder.HasOne(d => d.Client)
               .WithMany()
               .HasForeignKey(d => d.ClientId)
               .HasConstraintName("fk_designs_clients")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Artist)
               .WithMany()
               .HasForeignKey(d => d.ArtistId)
               .HasConstraintName("fk_designs_artists")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Revisions)
               .WithOne(r => r.Design)
               .HasForeignKey(r => r.DesignId)
               .HasConstraintName("fk_design_revisions_designs")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
