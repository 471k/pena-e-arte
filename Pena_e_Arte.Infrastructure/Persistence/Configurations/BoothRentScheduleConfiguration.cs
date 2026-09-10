using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class BoothRentScheduleConfiguration : TenantEntityConfiguration<BoothRentSchedule>
{
    protected override string TableName => "booth_rent_schedules";

    public override void Configure(EntityTypeBuilder<BoothRentSchedule> builder)
    {
        base.Configure(builder);

        builder.Property(b => b.AmountFixed).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.NextChargeDate).IsRequired();

        builder.HasIndex(b => new { b.StudioId, b.ArtistId });

        builder.HasOne(b => b.Artist)
               .WithMany()
               .HasForeignKey(b => b.ArtistId)
               .HasConstraintName("fk_booth_rent_schedules_artists")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Charges)
               .WithOne(c => c.Schedule)
               .HasForeignKey(c => c.BoothRentScheduleId)
               .HasConstraintName("fk_booth_rent_charges_schedules")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
