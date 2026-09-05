namespace Pena_e_Arte.Domain.Entities;

public class Conversation : TenantEntity
{
    private Conversation() { }

    public Guid ParticipantAUserId { get; private set; }
    public string ParticipantARole { get; private set; } = string.Empty;
    public Guid ParticipantBUserId { get; private set; }
    public string ParticipantBRole { get; private set; } = string.Empty;

    public DateTime? LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }
    public Guid? LastMessageSenderUserId { get; private set; }

    public ICollection<ChatMessage> Messages { get; private set; } = [];

    public static Conversation Create(
        Guid studioId, Guid userAId, string userARole, Guid userBId, string userBRole)
    {
        // Normalize so (studio, X, Y) and (studio, Y, X) always collide on the unique index
        // below — the caller does not (and should not) know or care which side of the pair
        // it's on.
        bool aFirst = userAId.CompareTo(userBId) <= 0;

        return new Conversation
        {
            StudioId = studioId,
            ParticipantAUserId = aFirst ? userAId : userBId,
            ParticipantARole = aFirst ? userARole : userBRole,
            ParticipantBUserId = aFirst ? userBId : userAId,
            ParticipantBRole = aFirst ? userBRole : userARole,
        };
    }

    public bool IsParticipant(Guid userId) =>
        userId == ParticipantAUserId || userId == ParticipantBUserId;

    public (Guid UserId, string Role) OtherParticipant(Guid userId) =>
        userId == ParticipantAUserId
            ? (ParticipantBUserId, ParticipantBRole)
            : (ParticipantAUserId, ParticipantARole);

    /// <summary>Denormalized preview fields for the inbox list — avoids a join/subquery per
    /// row just to show the last line and timestamp. Truncated to 140 chars; the full body
    /// lives only on the ChatMessage row.</summary>
    public void RecordLastMessage(Guid senderUserId, string body)
    {
        LastMessageAt = DateTime.UtcNow;
        LastMessageSenderUserId = senderUserId;
        LastMessagePreview = body.Length <= 140 ? body : body[..140];
    }
}
