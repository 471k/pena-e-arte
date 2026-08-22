namespace Pena_e_Arte.Contracts.Requests;

public record FileStudioConductReportRequest(
    Guid AppointmentId,
    string Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls = null);
