using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ClientNotificationPreferenceConfiguration : IEntityTypeConfiguration<ClientNotificationPreference>
{
    public void Configure(EntityTypeBuilder<ClientNotificationPreference> builder)
    {
        builder.ToTable("client_notification_preferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired();

        builder.HasIndex(p => new { p.UserId, p.StudioId, p.Type, p.Channel })
               .HasDatabaseName("uix_client_notification_preferences_user_studio_type_channel")
               .IsUnique();
    }
}
