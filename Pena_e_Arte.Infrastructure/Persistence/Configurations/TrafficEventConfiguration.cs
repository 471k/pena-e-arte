using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class TrafficEventConfiguration : IEntityTypeConfiguration<TrafficEvent>
{
    public void Configure(EntityTypeBuilder<TrafficEvent> builder)
    {
        builder.ToTable("traffic_events");
        builder.HasKey(t => t.Id).HasName("pk_traffic_events");

        builder.Property(t => t.Path).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Role).HasMaxLength(20);
        builder.Property(t => t.CountryCode).HasMaxLength(2);
        builder.Property(t => t.Country).HasMaxLength(100);
        builder.Property(t => t.RegionCode).HasMaxLength(10);
        builder.Property(t => t.Region).HasMaxLength(100);
        builder.Property(t => t.City).HasMaxLength(100);
        builder.Property(t => t.PostalCode).HasMaxLength(20);
        builder.Property(t => t.ContinentCode).HasMaxLength(2);
        builder.Property(t => t.Continent).HasMaxLength(100);
        builder.Property(t => t.TimeZone).HasMaxLength(64);
        builder.Property(t => t.AsnOrganization).HasMaxLength(256);
        builder.Property(t => t.IpHash).HasMaxLength(64);
        builder.Property(t => t.DeviceType).HasMaxLength(20);
        builder.Property(t => t.Browser).HasMaxLength(50);
        builder.Property(t => t.Os).HasMaxLength(50);

        // No HasQueryFilter — deliberate deviation from the standard TenantEntity shape.
        // StudioId is nullable (null = non-studio-scoped page); "who can read which rows"
        // is enforced in the query handlers (IssuerOnly), not here. Same non-tenant-scoped
        // shape as AuditLogEntry/HelpSearchLog/FeedbackReport.
        builder.HasIndex(t => t.CreatedAt).HasDatabaseName("ix_traffic_events_created_at");
        builder.HasIndex(t => new { t.StudioId, t.CreatedAt })
               .HasDatabaseName("ix_traffic_events_studio_created_at");
    }
}
