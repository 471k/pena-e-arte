using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioConfiguration : IEntityTypeConfiguration<Studio>
{
    public void Configure(EntityTypeBuilder<Studio> builder)
    {
        builder.ToTable("studios");
        builder.HasKey(s => s.Id).HasName("pk_studios");

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(100).IsRequired();
        builder.Property(s => s.City).HasMaxLength(100).IsRequired();
        builder.Property(s => s.OwnerEmail).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.CoverImageUrl).HasMaxLength(500);
        builder.Property(s => s.StripeAccountId).HasMaxLength(255);
        builder.Property(s => s.StripeCustomerId).HasMaxLength(255);

        builder.HasIndex(s => s.Slug).IsUnique().HasDatabaseName("ix_studios_slug");
        builder.HasIndex(s => s.IsActive).HasDatabaseName("ix_studios_is_active");
    }
}
