namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// Self-service "export my data" (§Phase C). One section per studio the caller is/was a
/// client at — the same cross-tenant fan-out as right-to-erasure, since "my data" means every
/// studio relationship, not just the active one. Never includes another client's data, another
/// studio's unrelated records, or any Identity/auth internals (password hash, tokens).
/// </summary>
public record ClientDataExportResponse(List<ClientDataExportStudioSection> Studios);

public record ClientDataExportStudioSection(
    Guid StudioId,
    string StudioName,
    ClientDataExportProfile Profile,
    List<ClientDataExportAppointment> Appointments,
    List<ClientDataExportConsentForm> ConsentForms,
    List<ClientDataExportTattooRecord> TattooRecords);

public record ClientDataExportProfile(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTime CreatedAt,
    DateOnly? DateOfBirth,
    string? Allergies,
    string? MedicalNotes);

public record ClientDataExportAppointment(
    Guid Id,
    DateTime Date,
    DateTime EndDate,
    string Status,
    decimal? PaymentAmount,
    string? PaymentStatus);

/// <summary>
/// SignedDocumentUrl is a time-limited signed R2 read URL, never the raw storage key and never
/// a permanently public link — generated fresh on each export request via IR2Service.
/// </summary>
public record ClientDataExportConsentForm(
    Guid Id,
    Guid AppointmentId,
    DateTime? SignedAt,
    string? SignedDocumentUrl);

public record ClientDataExportTattooRecord(
    Guid Id,
    string Description,
    string BodyLocation,
    DateTime CompletedAt,
    List<string> PhotoUrls);
