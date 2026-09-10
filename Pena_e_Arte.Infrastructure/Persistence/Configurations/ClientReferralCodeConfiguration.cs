using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientReferralCodeConfiguration : TenantEntityConfiguration<ClientReferralCode>
{
    protected override string TableName => "client_referral_codes";

    public override void Configure(EntityTypeBuilder<ClientReferralCode> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Code).HasMaxLength(12).IsRequired();
        builder.Property(c => c.RewardPercent).HasPrecision(5, 2);

        builder.HasIndex(c => new { c.StudioId, c.Code })
            .IsUnique()
            .HasDatabaseName("ix_client_referral_codes_studio_id_code");

        builder.HasIndex(c => new { c.StudioId, c.ReferrerClientId })
            .IsUnique()
            .HasDatabaseName("ix_client_referral_codes_studio_id_referrer_client_id");
    }
}
