using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class TrafficDailyAggregateConfiguration : IEntityTypeConfiguration<TrafficDailyAggregate>
{
    public void Configure(EntityTypeBuilder<TrafficDailyAggregate> builder)
    {
        builder.ToTable("traffic_daily_aggregates");
        builder.HasKey(t => t.Id).HasName("pk_traffic_daily_aggregates");

        builder.Property(t => t.Role).HasMaxLength(20);
        builder.Property(t => t.CountryCode).HasMaxLength(2);
        builder.Property(t => t.VisitCount).IsRequired();
        builder.Property(t => t.UniqueVisitorCount).IsRequired();

        // No HasQueryFilter — same non-tenant-scoped shape as TrafficEvent. Unique so
        // TrafficRollupJob can safely upsert one row per (Date, StudioId, Role, CountryCode).
        builder.HasIndex(t => new { t.Date, t.StudioId, t.Role, t.CountryCode })
               .IsUnique()
               .HasDatabaseName("ix_traffic_daily_aggregates_bucket");
    }
}
