using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PackageConfiguration : TenantEntityConfiguration<Package>
{
    protected override string TableName => "packages";

    public override void Configure(EntityTypeBuilder<Package> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.SessionCount).IsRequired();
    }
}
