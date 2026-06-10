using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientProfileConfiguration : TenantEntityConfiguration<ClientProfile>
{
    protected override string TableName => "client_profiles";

    public override void Configure(EntityTypeBuilder<ClientProfile> builder)
    {
        base.Configure(builder);

        builder.Property(cp => cp.MedicalNotes).HasMaxLength(4000);
        builder.Property(cp => cp.Allergies).HasMaxLength(1000);

        builder.Property(cp => cp.BodyMap)
               .HasColumnName("body_map")
               .HasColumnType("json")
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<BodyMap>(v, (JsonSerializerOptions?)null) ?? new BodyMap()
               )
               .Metadata.SetValueComparer(new ValueComparer<BodyMap>(
                   (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                          == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                   v => JsonSerializer.Deserialize<BodyMap>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null) ?? new BodyMap()
               ));

        builder.HasOne(cp => cp.Client)
               .WithOne(c => c.Profile)
               .HasForeignKey<ClientProfile>(cp => cp.ClientId)
               .HasConstraintName("fk_client_profiles_clients")
               .OnDelete(DeleteBehavior.Cascade);

        builder.Property(cp => cp.AllowCrossTenantRead)
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(cp => cp.CrossTenantOptInAt)
               .HasColumnType("datetime(6)");

        builder.HasIndex(cp => cp.ClientId)
               .IsUnique()
               .HasDatabaseName("ix_client_profiles_client_id");

        builder.HasIndex(cp => cp.AllowCrossTenantRead)
               .HasDatabaseName("ix_client_profiles_allow_cross_tenant_read");
    }
}
