using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates");
        builder.HasKey(r => r.Id).HasName("pk_exchange_rates");

        builder.Property(r => r.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.QuoteCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, 6).IsRequired();
        builder.Property(r => r.Source).HasMaxLength(20).IsRequired();

        // No HasQueryFilter — a public central-bank rate belongs to no studio. Unique so the fetch
        // job can never store the same published rate twice.
        builder.HasIndex(r => new { r.BaseCurrency, r.QuoteCurrency, r.RateDate, r.Source })
               .IsUnique()
               .HasDatabaseName("ix_exchange_rates_pair_date_source");
    }
}
