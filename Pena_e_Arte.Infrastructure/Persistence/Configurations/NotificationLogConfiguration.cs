using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class NotificationLogConfiguration : TenantEntityConfiguration<NotificationLog>
{
    protected override string TableName => "notification_logs";

    public override void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        base.Configure(builder);

        builder.Property(n => n.Channel)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(n => n.RecipientType)
               .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(n => n.Subject).HasMaxLength(500);
        builder.Property(n => n.Body).HasColumnType("text").IsRequired();

        builder.HasIndex(n => new { n.StudioId, n.RecipientId })
               .HasDatabaseName("ix_notification_logs_studio_recipient");

        builder.HasIndex(n => n.SentAt)
               .HasDatabaseName("ix_notification_logs_sent_at");

        builder.HasIndex(n => new { n.StudioId, n.CreatedAt })
               .HasDatabaseName("ix_notification_logs_studio_created_at");
    }
}
