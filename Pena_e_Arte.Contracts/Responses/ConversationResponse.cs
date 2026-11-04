namespace Pena_e_Arte.Contracts.Responses;

public record ConversationResponse(
    Guid Id,
    Guid OtherUserId,
    string OtherRole,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    bool LastMessageFromMe,
    int UnreadCount,
    DateTime CreatedAt,
    // The appointment the last message is about (id denormalized on the conversation; the label
    // is resolved from the appointment on every read). Both null when it has none.
    Guid? LastMessageAppointmentId = null,
    MessageAppointmentLabelResponse? LastMessageAppointment = null);
