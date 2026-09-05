using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PortfolioImageConfiguration : TenantEntityConfiguration<PortfolioImage>
{
    protected override string TableName => "PortfolioImages";

    public override void Configure(EntityTypeBuilder<PortfolioImage> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(p => p.Style).HasMaxLength(50).IsRequired(false);
        builder.Property(p => p.Category).HasMaxLength(20).IsRequired(false);

        builder.HasIndex(p => p.ArtistId)
               .HasDatabaseName("ix_portfolio_images_artist_id");

        builder.HasOne(p => p.Artist)
               .WithMany(a => a.Portfolio)
               .HasForeignKey(p => p.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
