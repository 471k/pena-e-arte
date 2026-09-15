using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ServiceConfiguration : TenantEntityConfiguration<Service>
{
    protected override string TableName => "services";

    public override void Configure(EntityTypeBuilder<Service> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Price).HasColumnType("decimal(18,2)");
        builder.Property(s => s.DepositAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(s => new { s.StudioId, s.IsActive })
               .HasDatabaseName("ix_services_studio_id_is_active");
    }
}
