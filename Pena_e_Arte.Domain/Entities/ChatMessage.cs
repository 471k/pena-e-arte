namespace Pena_e_Arte.Domain.Entities;

public class ChatMessage : TenantEntity
{
    private ChatMessage() { }

    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderRole { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime? ReadAt { get; private set; }

    /// <summary>The appointment this message is about, when sent from the client appointment page
    /// (DEC06), so the client need not re-describe it. Null for every message sent before this
    /// existed and for any message not about one appointment.</summary>
    public Guid? AppointmentId { get; private set; }

    public static ChatMessage Create(
        Guid studioId, Guid conversationId, Guid senderUserId, string senderRole, string body,
        Guid? appointmentId = null) =>
        new()
        {
            StudioId = studioId,
            ConversationId = conversationId,
            SenderUserId = senderUserId,
            SenderRole = senderRole,
            Body = body.Trim(),
            AppointmentId = appointmentId,
        };

    /// <summary>Idempotent — calling this on an already-read message is a no-op.</summary>
    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
