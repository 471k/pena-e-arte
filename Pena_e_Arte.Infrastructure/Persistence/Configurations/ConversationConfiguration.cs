using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : TenantEntityConfiguration<Conversation>
{
    protected override string TableName => "conversations";

    public override void Configure(EntityTypeBuilder<Conversation> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.ParticipantARole).HasMaxLength(20).IsRequired();
        builder.Property(c => c.ParticipantBRole).HasMaxLength(20).IsRequired();
        builder.Property(c => c.LastMessagePreview).HasMaxLength(140);

        builder.HasIndex(c => new { c.StudioId, c.ParticipantAUserId, c.ParticipantBUserId })
               .IsUnique()
               .HasDatabaseName("ix_conversations_studio_participants");

        // Inbox listing: "my conversations, most recent first" — this is the hot query.
        builder.HasIndex(c => new { c.StudioId, c.ParticipantAUserId, c.LastMessageAt })
               .HasDatabaseName("ix_conversations_studio_participant_a_last_message");
        builder.HasIndex(c => new { c.StudioId, c.ParticipantBUserId, c.LastMessageAt })
               .HasDatabaseName("ix_conversations_studio_participant_b_last_message");

        builder.HasMany(c => c.Messages)
               .WithOne()
               .HasForeignKey(m => m.ConversationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
