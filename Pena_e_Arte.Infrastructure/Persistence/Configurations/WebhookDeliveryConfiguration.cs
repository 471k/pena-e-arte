using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class WebhookDeliveryConfiguration : TenantEntityConfiguration<WebhookDelivery>
{
    protected override string TableName => "webhook_deliveries";

    public override void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        base.Configure(builder);

        builder.Property(d => d.EventType).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ErrorMessage).HasMaxLength(1000);

        builder.HasIndex(d => new { d.WebhookEndpointId, d.AttemptedAt })
               .HasDatabaseName("ix_webhook_deliveries_endpoint_attempted_at");
    }
}
