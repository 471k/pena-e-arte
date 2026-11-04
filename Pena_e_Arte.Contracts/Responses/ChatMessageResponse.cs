namespace Pena_e_Arte.Contracts.Responses;

public record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderRole,
    string Body,
    DateTime CreatedAt,
    DateTime? ReadAt,
    Guid? AppointmentId = null,
    // Resolved on reads (GET messages). The send response and the realtime push carry the id
    // only; the client refetches the thread after either, so it never renders from them.
    MessageAppointmentLabelResponse? Appointment = null);
