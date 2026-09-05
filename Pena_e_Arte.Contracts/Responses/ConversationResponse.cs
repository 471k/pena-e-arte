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
    DateTime CreatedAt);
