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
        builder.Property(i => i.Specializations).HasMaxLength(500);
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
