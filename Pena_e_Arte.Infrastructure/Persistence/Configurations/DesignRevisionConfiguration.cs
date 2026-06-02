using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class DesignRevisionConfiguration : TenantEntityConfiguration<DesignRevision>
{
    protected override string TableName => "design_revisions";

    public override void Configure(EntityTypeBuilder<DesignRevision> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.FileUrl).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);

        builder.HasIndex(r => new { r.DesignId, r.VersionNumber })
               .IsUnique()
               .HasDatabaseName("ix_design_revisions_design_version");
    }
}
