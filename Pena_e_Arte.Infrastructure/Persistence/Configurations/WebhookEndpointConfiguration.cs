using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class WebhookEndpointConfiguration : TenantEntityConfiguration<WebhookEndpoint>
{
    protected override string TableName => "webhook_endpoints";

    public override void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        base.Configure(builder);

        builder.Property(w => w.Url).HasMaxLength(2048).IsRequired();
        builder.Property(w => w.EncryptedSecret).HasMaxLength(512).IsRequired();
    }
}
