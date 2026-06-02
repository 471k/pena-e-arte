using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public abstract class TenantEntityConfiguration<T> : IEntityTypeConfiguration<T>
    where T : TenantEntity
{
    protected abstract string TableName { get; }

    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.Id).HasName($"pk_{TableName}");
        builder.Property(e => e.StudioId).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.HasIndex(e => e.StudioId).HasDatabaseName($"ix_{TableName}_studio_id");
    }
}
