using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class WaitlistConfiguration : TenantEntityConfiguration<Waitlist>
{
    protected override string TableName => "waitlist_entries";

    public override void Configure(EntityTypeBuilder<Waitlist> builder)
    {
        base.Configure(builder);

        builder.Property(w => w.GuestName).HasMaxLength(200);
        builder.Property(w => w.GuestEmail).HasMaxLength(320);
        builder.Property(w => w.GuestPhone).HasMaxLength(50);
        builder.Property(w => w.Notes).HasMaxLength(1000);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(w => new { w.StudioId, w.Status });
        builder.HasIndex(w => new { w.StudioId, w.ArtistId, w.Status });

        builder.HasOne(w => w.Artist)
               .WithMany()
               .HasForeignKey(w => w.ArtistId)
               .HasConstraintName("fk_waitlist_entries_artists")
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(w => w.Client)
               .WithMany()
               .HasForeignKey(w => w.ClientId)
               .HasConstraintName("fk_waitlist_entries_clients")
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);
    }
}
