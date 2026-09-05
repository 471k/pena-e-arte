namespace Pena_e_Arte.Domain.Entities;

public class ChatMessage : TenantEntity
{
    private ChatMessage() { }

    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderRole { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime? ReadAt { get; private set; }

    public static ChatMessage Create(
        Guid studioId, Guid conversationId, Guid senderUserId, string senderRole, string body) =>
        new()
        {
            StudioId = studioId,
            ConversationId = conversationId,
            SenderUserId = senderUserId,
            SenderRole = senderRole,
            Body = body.Trim(),
        };

    /// <summary>Idempotent — calling this on an already-read message is a no-op.</summary>
    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
