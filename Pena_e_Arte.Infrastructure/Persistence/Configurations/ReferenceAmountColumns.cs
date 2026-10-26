using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

/// <summary>
/// The five nullable lek-reference columns shared by Payment, GiftCard and PackagePurchase
/// (IHasReferenceAmount). Configured by name so EF maps the entity's own properties.
/// </summary>
internal static class ReferenceAmountColumns
{
    public static void ConfigureReferenceAmount<T>(this EntityTypeBuilder<T> builder)
        where T : class, IHasReferenceAmount
    {
        builder.Property<string?>(nameof(IHasReferenceAmount.ReferenceCurrency)).HasMaxLength(3);
        builder.Property<decimal?>(nameof(IHasReferenceAmount.ReferenceRate)).HasColumnType("decimal(18,6)");
        builder.Property<DateOnly?>(nameof(IHasReferenceAmount.ReferenceRateDate));
        builder.Property<string?>(nameof(IHasReferenceAmount.ReferenceRateSource)).HasMaxLength(20);
        builder.Property<decimal?>(nameof(IHasReferenceAmount.ReferenceAmount)).HasColumnType("decimal(18,4)");
    }
}
