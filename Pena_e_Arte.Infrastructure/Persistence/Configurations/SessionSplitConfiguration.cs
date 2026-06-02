using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SessionSplitConfiguration : TenantEntityConfiguration<SessionSplit>
{
    protected override string TableName => "session_splits";

    public override void Configure(EntityTypeBuilder<SessionSplit> builder)
    {
        base.Configure(builder);

        builder.Property(ss => ss.Label).HasMaxLength(100).IsRequired();
        builder.Property(ss => ss.Amount).HasColumnType("decimal(18,2)").IsRequired();
    }
}
