using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientReferralRedemptionConfiguration : TenantEntityConfiguration<ClientReferralRedemption>
{
    protected override string TableName => "client_referral_redemptions";

    public override void Configure(EntityTypeBuilder<ClientReferralRedemption> builder)
    {
        base.Configure(builder);

        // One redemption per client per code — DB-enforced, not just application-checked.
        builder.HasIndex(r => new { r.ClientReferralCodeId, r.RedeemedByClientId })
            .IsUnique()
            .HasDatabaseName("ix_client_referral_redemptions_code_id_redeemed_by_client_id");
    }
}
