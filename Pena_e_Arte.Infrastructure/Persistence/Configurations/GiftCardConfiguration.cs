using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class GiftCardConfiguration : TenantEntityConfiguration<GiftCard>
{
    protected override string TableName => "gift_cards";

    public override void Configure(EntityTypeBuilder<GiftCard> builder)
    {
        base.Configure(builder);

        builder.Property(g => g.Code).HasMaxLength(20).IsRequired();
        builder.Property(g => g.InitialBalance).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(g => g.RemainingBalance).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(g => g.PurchaserEmail).HasMaxLength(320).IsRequired();
        builder.Property(g => g.RecipientEmail).HasMaxLength(320);
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(g => g.ProviderReferenceId).HasMaxLength(255);
        builder.Property(g => g.ClientSecret).HasMaxLength(500);
        builder.Property(g => g.Provider).HasMaxLength(32).IsRequired();

        // Codes are looked up per studio; not globally unique (studio-scoped balances).
        builder.HasIndex(g => new { g.StudioId, g.Code }).IsUnique().HasDatabaseName("ux_gift_cards_studio_code");
    }
}
