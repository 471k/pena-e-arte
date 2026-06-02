using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class TattooRecordConfiguration : TenantEntityConfiguration<TattooRecord>
{
    protected override string TableName => "tattoo_records";

    public override void Configure(EntityTypeBuilder<TattooRecord> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Description).HasMaxLength(2000).IsRequired();
        builder.Property(t => t.BodyLocation).HasMaxLength(200).IsRequired();

        builder.Property(t => t.PhotoUrls)
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
               .HasColumnType("json");

        builder.HasOne(t => t.Client)
               .WithMany(c => c.TattooRecords)
               .HasForeignKey(t => t.ClientId)
               .HasConstraintName("fk_tattoo_records_clients")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Artist)
               .WithMany(a => a.TattooRecords)
               .HasForeignKey(t => t.ArtistId)
               .HasConstraintName("fk_tattoo_records_artists")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
