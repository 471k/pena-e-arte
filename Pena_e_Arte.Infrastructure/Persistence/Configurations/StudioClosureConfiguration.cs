using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioClosureConfiguration : IEntityTypeConfiguration<StudioClosure>
{
    public void Configure(EntityTypeBuilder<StudioClosure> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Reason)
               .HasMaxLength(500);

        builder.HasIndex(c => new { c.StudioId, c.StartDate, c.EndDate })
               .HasDatabaseName("ix_studio_closures_studio_dates");
    }
}
