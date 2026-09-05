namespace Pena_e_Arte.Contracts.Responses;

public record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderRole,
    string Body,
    DateTime CreatedAt,
    DateTime? ReadAt);
