using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class DesignApprovalConfiguration : TenantEntityConfiguration<DesignApproval>
{
    protected override string TableName => "design_approvals";

    public override void Configure(EntityTypeBuilder<DesignApproval> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.Status)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(a => a.ClientNotes).HasMaxLength(2000);

        builder.HasOne(a => a.DesignRevision)
               .WithOne(r => r.Approval)
               .HasForeignKey<DesignApproval>(a => a.DesignRevisionId)
               .HasConstraintName("fk_design_approvals_design_revisions")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.DesignRevisionId)
               .IsUnique()
               .HasDatabaseName("ix_design_approvals_design_revision_id");
    }
}
