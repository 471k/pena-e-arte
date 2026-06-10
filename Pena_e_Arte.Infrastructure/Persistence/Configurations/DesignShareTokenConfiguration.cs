using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class DesignShareTokenConfiguration : TenantEntityConfiguration<DesignShareToken>
{
    protected override string TableName => "design_share_tokens";

    public override void Configure(EntityTypeBuilder<DesignShareToken> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Token).HasMaxLength(32).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();

        builder.HasIndex(t => t.Token)
               .IsUnique()
               .HasDatabaseName("ix_design_share_tokens_token");

        builder.HasIndex(t => t.ExpiresAt)
               .HasDatabaseName("ix_design_share_tokens_expires_at");

        builder.HasOne(t => t.DesignRevision)
               .WithMany()
               .HasForeignKey(t => t.DesignRevisionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
