using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioJoinInviteConfiguration : IEntityTypeConfiguration<StudioJoinInvite>
{
    public void Configure(EntityTypeBuilder<StudioJoinInvite> builder)
    {
        builder.ToTable("studio_join_invites");
        builder.HasKey(i => i.Id).HasName("pk_studio_join_invites");

        builder.Property(i => i.InvitedEmail).HasMaxLength(256).IsRequired();
        builder.Property(i => i.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(i => i.LastName).HasMaxLength(100).IsRequired();
        // Matches Artist.Specializations (ArtistConfiguration) exactly — this value is copied
        // verbatim onto the real Artist row at accept time, so the columns must agree.
        builder.Property(i => i.Specializations)
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
               .HasColumnType("json");
        builder.Property(i => i.HourlyRate).HasColumnType("decimal(18,2)");
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(i => i.Studio)
               .WithMany()
               .HasForeignKey(i => i.StudioId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.InvitedEmail).HasDatabaseName("ix_studio_join_invites_invited_email");

        // Enforced in InviteSoloArtistToJoinHandler, not as a filtered unique index — MySQL/EF
        // Core's provider does not support a filtered ("Status = Pending") unique index cleanly
        // here, so "no duplicate pending invite" is a handler-level check instead.
        builder.HasIndex(i => new { i.StudioId, i.InvitedEmail })
               .HasDatabaseName("ix_studio_join_invites_studio_id_invited_email");
    }
}
