using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientConfiguration : TenantEntityConfiguration<Client>
{
    protected override string TableName => "clients";

    public override void Configure(EntityTypeBuilder<Client> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);

        builder.HasIndex(c => new { c.StudioId, c.Email })
               .IsUnique()
               .HasDatabaseName("ix_clients_studio_email");
    }
}
