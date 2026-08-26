namespace Pena_e_Arte.Contracts.Responses;

public record ConversationContactResponse(
    Guid UserId,
    string Role,
    string DisplayName,
    string? AvatarUrl,
    Guid? ExistingConversationId);
