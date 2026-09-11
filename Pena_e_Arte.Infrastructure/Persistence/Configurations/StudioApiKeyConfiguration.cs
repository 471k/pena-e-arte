using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioApiKeyConfiguration : TenantEntityConfiguration<StudioApiKey>
{
    protected override string TableName => "studio_api_keys";

    public override void Configure(EntityTypeBuilder<StudioApiKey> builder)
    {
        base.Configure(builder);

        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(k => k.KeyPrefix).HasMaxLength(16).IsRequired();

        // Looked up by hash alone, before the caller's tenant is known — same
        // cross-tenant-lookup shape as refresh tokens / StudioJoinInvite resolution.
        builder.HasIndex(k => k.KeyHash)
               .IsUnique()
               .HasDatabaseName("ix_studio_api_keys_key_hash");
    }
}
