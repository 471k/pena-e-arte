namespace Pena_e_Arte.Contracts.Responses;

/// <param name="ApprovalStatus">The client's decision on THIS revision: Pending, Approved, ChangesRequested
/// or Expired (no answer in time); null when it has never been reviewed.</param>
/// <param name="ApprovalNotes">The client's feedback on this revision, if any.</param>
/// <param name="ReviewedAt">When this revision was approved, sent back, or expired (UTC); null when it has
/// never been reviewed (E04c, A05: per-revision history, read from the existing approval row).</param>
public record DesignRevisionResponse(
    Guid Id,
    Guid DesignId,
    int VersionNumber,
    string FileUrl,
    string? Notes,
    DateTime UploadedAt,
    string? ApprovalStatus,
    string? ApprovalNotes,
    DateTime? ReviewedAt = null);
