using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class DepositRuleConfiguration : TenantEntityConfiguration<DepositRule>
{
    protected override string TableName => "deposit_rules";

    public override void Configure(EntityTypeBuilder<DepositRule> builder)
    {
        base.Configure(builder);

        builder.Property(d => d.AmountFixed).HasColumnType("decimal(18,2)");
        builder.Property(d => d.AmountPercent).HasColumnType("decimal(5,2)");
    }
}
