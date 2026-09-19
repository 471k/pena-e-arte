using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SavedPaymentMethodConfiguration : TenantEntityConfiguration<SavedPaymentMethod>
{
    protected override string TableName => "saved_payment_methods";

    public override void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.Provider).IsRequired().HasMaxLength(20);
        builder.Property(s => s.ProviderCardTokenId).IsRequired().HasMaxLength(200);
        builder.Property(s => s.CardBrand).HasMaxLength(30);
        builder.Property(s => s.MaskedPan).HasMaxLength(30);
        builder.Property(s => s.ExpiryMonth).HasMaxLength(2);
        builder.Property(s => s.ExpiryYear).HasMaxLength(4);

        // Idempotent add-retries: a retried AddCardForm submit for the same card resolves to
        // the same row instead of a duplicate.
        builder.HasIndex(s => new { s.StudioId, s.ClientId, s.ProviderCardTokenId })
               .IsUnique()
               .HasDatabaseName("ux_saved_payment_methods_studio_client_token");

        builder.HasOne(s => s.Client)
               .WithMany()
               .HasForeignKey(s => s.ClientId)
               .HasConstraintName("fk_saved_payment_methods_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
