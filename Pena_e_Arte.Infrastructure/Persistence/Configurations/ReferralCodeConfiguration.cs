using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> builder)
    {
        builder.ToTable("referral_codes");
        builder.HasKey(r => r.Id).HasName("pk_referral_codes");

        builder.Property(r => r.Code).HasMaxLength(8).IsRequired();
        builder.Property(r => r.StudioId).IsRequired();

        builder.HasIndex(r => r.Code).IsUnique().HasDatabaseName("ix_referral_codes_code");
        builder.HasIndex(r => r.StudioId).HasDatabaseName("ix_referral_codes_studio_id");
    }
}
