using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientReferralRewardConfiguration : TenantEntityConfiguration<ClientReferralReward>
{
    protected override string TableName => "client_referral_rewards";

    public override void Configure(EntityTypeBuilder<ClientReferralReward> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.RewardPercent).HasPrecision(5, 2);

        builder.HasIndex(r => new { r.StudioId, r.ClientId })
            .HasDatabaseName("ix_client_referral_rewards_studio_id_client_id");
    }
}
