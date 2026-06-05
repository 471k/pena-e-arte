namespace Pena_e_Arte.Contracts.Responses;

public record DesignRevisionResponse(
    Guid     Id,
    Guid     DesignId,
    int      VersionNumber,
    string   FileUrl,
    string?  Notes,
    DateTime UploadedAt,
    string?  ApprovalStatus,
    string?  ApprovalNotes);
