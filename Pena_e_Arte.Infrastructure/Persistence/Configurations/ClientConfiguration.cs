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
        builder.Property(c => c.SmsOptOut).HasDefaultValue(false).IsRequired();

        builder.HasIndex(c => new { c.StudioId, c.Email })
               .IsUnique()
               .HasDatabaseName("ix_clients_studio_email");

        // Supports cross-studio membership lookups by UserId (multi-studio client
        // support — see ClientAccountExtensions.FindClientForUserAtStudioAsync).
        builder.HasIndex(c => c.UserId)
               .HasDatabaseName("ix_clients_user_id");

        builder.HasIndex(c => new { c.StudioId, c.ArtistId })
               .HasDatabaseName("ix_clients_studio_artist");

        builder.HasOne(c => c.Artist)
               .WithMany(a => a.Clients)
               .HasForeignKey(c => c.ArtistId)
               .HasConstraintName("fk_clients_artists")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
