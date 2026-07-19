using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ReferralRedemptionConfiguration : IEntityTypeConfiguration<ReferralRedemption>
{
    public void Configure(EntityTypeBuilder<ReferralRedemption> builder)
    {
        builder.ToTable("referral_redemptions");
        builder.HasKey(r => r.Id).HasName("pk_referral_redemptions");

        builder.Property(r => r.ReferralCodeId).IsRequired();
        builder.Property(r => r.NewStudioId).IsRequired();

        builder.Property(r => r.ReferrerRewardApplied)
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(r => r.ReferrerRewardCouponId)
               .HasMaxLength(255);

        builder.HasIndex(r => r.ReferralCodeId).HasDatabaseName("ix_referral_redemptions_code_id");
        builder.HasIndex(r => r.NewStudioId).IsUnique().HasDatabaseName("ix_referral_redemptions_new_studio_id");
    }
}
