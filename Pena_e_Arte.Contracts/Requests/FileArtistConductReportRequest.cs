namespace Pena_e_Arte.Contracts.Requests;

public record FileArtistConductReportRequest(
    Guid AppointmentId,
    string Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls = null);
