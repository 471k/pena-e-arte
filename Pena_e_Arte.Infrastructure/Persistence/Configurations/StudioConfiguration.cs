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
        builder.Property(s => s.StripeCustomerId).HasMaxLength(255);
        builder.Property(s => s.StorageUsageBytes).HasDefaultValue(0L);
        builder.Property(s => s.Nipt).HasMaxLength(10);
        builder.Property(s => s.IsSolo).HasDefaultValue(false);
        builder.Property(s => s.IsPublished).HasDefaultValue(true);

        builder.HasIndex(s => s.Slug).IsUnique().HasDatabaseName("ix_studios_slug");
        builder.HasIndex(s => s.IsActive).HasDatabaseName("ix_studios_is_active");
        builder.HasIndex(s => s.PendingReferralCodeId).HasDatabaseName("ix_studios_pending_referral_code_id");

        // Not unique: the business rule is "no two DIFFERENT owners may share a NIPT",
        // not "no two studios may share a NIPT" — the same owner legitimately reuses
        // their NIPT across multiple locations (see RegisterStudioHandler /
        // UpdateMyStudioHandler). A plain SQL unique index cannot express that
        // conditional exception, so uniqueness is enforced at the application layer;
        // this index exists only to make the handlers' Nipt lookups efficient.
        builder.HasIndex(s => s.Nipt).HasDatabaseName("ix_studios_nipt");
    }
}
