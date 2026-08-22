namespace Pena_e_Arte.Contracts.Responses;

// ReporterUserId/ReporterName are nullable and populated ONLY when the caller is authorized to
// see reporter identity (owner, issuer). For an artist-scoped read they are always null — see
// ConductReportProjections.ToRedactedResponse. This redaction happens server-side in the
// handler's projection, never client-side — do not ship a version of this response that
// includes the fields and relies on the frontend to hide them.
public record ConductReportResponse(
    Guid Id,
    Guid StudioId,
    string StudioName,
    Guid? ArtistId,
    string? ArtistName,
    Guid AppointmentId,
    DateTime AppointmentDate,
    string Category,
    bool IsHighSeverity,
    string Reason,
    IReadOnlyList<string> AttachmentUrls,
    string Status,
    string? ResolutionNote,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    Guid? ReporterUserId,
    string? ReporterName);
