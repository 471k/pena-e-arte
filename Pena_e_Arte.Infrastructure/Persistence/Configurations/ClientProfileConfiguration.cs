using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientProfileConfiguration : TenantEntityConfiguration<ClientProfile>
{
    protected override string TableName => "client_profiles";

    public override void Configure(EntityTypeBuilder<ClientProfile> builder)
    {
        base.Configure(builder);

        builder.Property(cp => cp.MedicalNotes).HasMaxLength(4000);
        builder.Property(cp => cp.Allergies).HasMaxLength(1000);

        builder.OwnsOne(cp => cp.BodyMap, b => b.ToJson());

        builder.HasOne(cp => cp.Client)
               .WithOne(c => c.Profile)
               .HasForeignKey<ClientProfile>(cp => cp.ClientId)
               .HasConstraintName("fk_client_profiles_clients")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(cp => cp.ClientId)
               .IsUnique()
               .HasDatabaseName("ix_client_profiles_client_id");
    }
}
