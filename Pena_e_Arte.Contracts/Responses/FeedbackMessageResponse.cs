namespace Pena_e_Arte.Contracts.Responses;

public record FeedbackMessageResponse(
    Guid     Id,
    Guid     FeedbackReportId,
    Guid     AuthorUserId,
    string   AuthorRole,
    string   Body,
    DateTime CreatedAt);
