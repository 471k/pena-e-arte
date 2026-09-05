using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : TenantEntityConfiguration<ChatMessage>
{
    protected override string TableName => "chat_messages";

    public override void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.SenderRole).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(2000).IsRequired();

        // Cursor pagination for one thread ("messages before X") + the unread-count query
        // ("unread messages in conversations I'm part of") both hit this.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
               .HasDatabaseName("ix_chat_messages_conversation_created");
        builder.HasIndex(m => new { m.ConversationId, m.SenderUserId, m.ReadAt })
               .HasDatabaseName("ix_chat_messages_conversation_sender_read");
    }
}
