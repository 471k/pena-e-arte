using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioNotificationPreferenceConfiguration : TenantEntityConfiguration<StudioNotificationPreference>
{
    protected override string TableName => "studio_notification_preferences";

    public override void Configure(EntityTypeBuilder<StudioNotificationPreference> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired();

        builder.HasIndex(p => new { p.StudioId, p.Type, p.Channel })
               .HasDatabaseName("uix_studio_notification_preferences_studio_type_channel")
               .IsUnique();
    }
}
