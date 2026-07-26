namespace Pena_e_Arte.Contracts.Responses;

public record NotificationLogResponse(
    Guid Id,
    Guid RecipientId,
    string? RecipientName,
    string Channel,
    string? Subject,
    string Body,
    DateTime? SentAt,
    bool IsSuccess,
    DateTime CreatedAt);
