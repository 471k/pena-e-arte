using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : TenantEntityConfiguration<Campaign>
{
    protected override string TableName => "campaigns";

    public override void Configure(EntityTypeBuilder<Campaign> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Subject).HasMaxLength(200).IsRequired();
        builder.Property(c => c.BodyHtml).HasColumnType("mediumtext").IsRequired();
        builder.Property(c => c.Audience).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(c => c.CustomClientIds)
               .HasColumnName("custom_client_ids")
               .HasColumnType("json")
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
               )
               .Metadata.SetValueComparer(new ValueComparer<List<Guid>>(
                   (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                          == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                   v => JsonSerializer.Deserialize<List<Guid>>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null) ?? new List<Guid>()
               ));

        builder.HasIndex(c => new { c.StudioId, c.Status })
               .HasDatabaseName("ix_campaigns_studio_id_status");
    }
}
