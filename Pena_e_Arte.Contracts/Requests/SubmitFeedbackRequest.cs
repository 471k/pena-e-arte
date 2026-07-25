namespace Pena_e_Arte.Contracts.Requests;

public record SubmitFeedbackRequest(
    string Type,
    string Title,
    string Body,
    IReadOnlyList<string>? AttachmentUrls = null);
