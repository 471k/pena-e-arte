using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class BoothRentChargeConfiguration : TenantEntityConfiguration<BoothRentCharge>
{
    protected override string TableName => "booth_rent_charges";

    public override void Configure(EntityTypeBuilder<BoothRentCharge> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(c => c.ChargedDate).IsRequired();
        builder.Property(c => c.SettledNote).HasMaxLength(500);

        builder.HasIndex(c => new { c.StudioId, c.ArtistId });
        builder.HasIndex(c => new { c.StudioId, c.IsSettled });

        builder.HasOne(c => c.Artist)
               .WithMany()
               .HasForeignKey(c => c.ArtistId)
               .HasConstraintName("fk_booth_rent_charges_artists")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
