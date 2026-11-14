namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// A feedback report as its submitter sees it (GET /api/v1/feedback/mine and the POST /api/v1/feedback
/// response). Deliberately has no AdminNote: that field is the platform admin's internal note
/// (Help: "only visible to you, not the sender"), so it must not be in this type at all rather than
/// being nulled out. The admin inbox uses <see cref="FeedbackReportResponse"/>, which keeps it.
/// </summary>
public record MyFeedbackReportResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string Status,
    string StudioName,
    string SubmitterRole,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    IReadOnlyList<string>? AttachmentUrls = null);
