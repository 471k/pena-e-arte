using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class FeedbackReport
{
    private FeedbackReport() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid StudioId { get; private set; }
    public Guid SubmitterUserId { get; private set; }
    public string SubmitterRole { get; private set; } = string.Empty;
    public string StudioName { get; private set; } = string.Empty;
    public FeedbackType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public FeedbackStatus Status { get; private set; } = FeedbackStatus.Open;
    public string? IssuerNote { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; private set; }

    // Optional screenshots/short video clips uploaded via the same R2 presign flow
    // used elsewhere (Design revisions, appointment reference images).
    public List<string> AttachmentUrls { get; private set; } = [];

    public ICollection<FeedbackMessage> Messages { get; private set; } = [];

    public static FeedbackReport Create(
        Guid studioId,
        Guid submitterUserId,
        string submitterRole,
        string studioName,
        FeedbackType type,
        string title,
        string body,
        IReadOnlyList<string>? attachmentUrls = null)
    {
        return new FeedbackReport
        {
            StudioId = studioId,
            SubmitterUserId = submitterUserId,
            SubmitterRole = submitterRole,
            StudioName = studioName,
            Type = type,
            Title = title.Trim(),
            Body = body.Trim(),
            AttachmentUrls = attachmentUrls?.ToList() ?? [],
        };
    }

    public void UpdateStatus(FeedbackStatus status, string? issuerNote)
    {
        Status = status;
        IssuerNote = issuerNote?.Trim();
        ResolvedAt = status is FeedbackStatus.Resolved or FeedbackStatus.Dismissed
            ? DateTime.UtcNow
            : null;
    }

    /// <summary>Issuer can access any ticket; everyone else only their own, within their own studio.</summary>
    public bool IsAccessibleBy(Guid userId, Guid studioId, string role) =>
        string.Equals(role, "issuer", StringComparison.OrdinalIgnoreCase)
        || (SubmitterUserId == userId && StudioId == studioId);
}
