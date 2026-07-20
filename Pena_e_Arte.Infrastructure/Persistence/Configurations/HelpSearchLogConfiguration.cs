using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class HelpSearchLogConfiguration : TenantEntityConfiguration<HelpSearchLog>
{
    protected override string TableName => "help_search_logs";

    public override void Configure(EntityTypeBuilder<HelpSearchLog> builder)
    {
        base.Configure(builder);

        builder.Property(h => h.Role).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Query).HasMaxLength(200).IsRequired();
        builder.Property(h => h.ResultCount).IsRequired();

        builder.HasIndex(h => new { h.Query, h.CreatedAt })
               .HasDatabaseName("ix_help_search_logs_query_created_at");

        builder.HasIndex(h => new { h.StudioId, h.CreatedAt })
               .HasDatabaseName("ix_help_search_logs_studio_created_at");
    }
}
