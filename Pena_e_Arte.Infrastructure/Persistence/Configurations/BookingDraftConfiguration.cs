using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class BookingDraftConfiguration : TenantEntityConfiguration<BookingDraft>
{
    /// <summary>The largest payload a draft may hold. The form's text fields fit well inside it.</summary>
    public const int MaxPayloadLength = 16000;

    protected override string TableName => "BookingDrafts";

    public override void Configure(EntityTypeBuilder<BookingDraft> builder)
    {
        base.Configure(builder);

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.PayloadJson).HasMaxLength(MaxPayloadLength).IsRequired();
        builder.Property(d => d.ExpiresAt).IsRequired();

        // One draft per person per studio.
        builder.HasIndex(d => new { d.StudioId, d.UserId })
               .IsUnique()
               .HasDatabaseName("ux_booking_drafts_studio_user");
        builder.HasIndex(d => d.ExpiresAt)
               .HasDatabaseName("ix_booking_drafts_expires_at");

        builder.HasMany(d => d.Attachments)
               .WithOne(a => a.Draft)
               .HasForeignKey(a => a.DraftId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BookingDraftAttachmentConfiguration : TenantEntityConfiguration<BookingDraftAttachment>
{
    protected override string TableName => "BookingDraftAttachments";

    public override void Configure(EntityTypeBuilder<BookingDraftAttachment> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(a => a.Category)
               .HasConversion<string>()
               .HasMaxLength(16)
               .HasDefaultValue(Domain.Enums.AppointmentAttachmentCategory.Reference);

        builder.HasIndex(a => a.DraftId)
               .HasDatabaseName("ix_booking_draft_attachments_draft_id");
    }
}
