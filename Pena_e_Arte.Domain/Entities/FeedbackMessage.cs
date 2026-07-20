namespace Pena_e_Arte.Domain.Entities;

public class FeedbackMessage
{
    public Guid     Id               { get; private set; } = Guid.NewGuid();
    public Guid     FeedbackReportId { get; private set; }
    public Guid     AuthorUserId     { get; private set; }
    public string   AuthorRole       { get; private set; } = string.Empty;
    public string   Body             { get; private set; } = string.Empty;
    public DateTime CreatedAt        { get; private set; } = DateTime.UtcNow;

    private FeedbackMessage() { }

    public static FeedbackMessage Create(Guid feedbackReportId, Guid authorUserId, string authorRole, string body) =>
        new()
        {
            FeedbackReportId = feedbackReportId,
            AuthorUserId     = authorUserId,
            AuthorRole       = authorRole,
            Body             = body.Trim(),
        };
}
