namespace Pena_e_Arte.Contracts.Responses;

public record FeedbackReportResponse(
    Guid      Id,
    string    Type,
    string    Title,
    string    Body,
    string    Status,
    string    StudioName,
    string    SubmitterRole,
    string?   IssuerNote,
    DateTime  CreatedAt,
    DateTime? ResolvedAt);
