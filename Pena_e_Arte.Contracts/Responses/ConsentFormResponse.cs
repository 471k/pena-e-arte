namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Returned by GET /api/v1/consent-forms (list). ClientName is always populated.</summary>
public record ConsentFormResponse(
    Guid Id,
    Guid StudioId,
    Guid ClientId,
    Guid AppointmentId,
    string? FileUrl,
    string? SignatureData,
    DateTime? SignedAt,
    DateTime CreatedAt,
    string ClientName);

/// <summary>
/// Returned by GET /api/v1/consent-forms/{id} (detail).
/// Includes resolved human-readable fields for display without UUID lookup.
/// </summary>
public record ConsentFormDetailResponse(
    Guid Id,
    Guid StudioId,
    Guid ClientId,
    Guid AppointmentId,
    string? FileUrl,
    string? SignatureData,
    DateTime? SignedAt,
    DateTime CreatedAt,
    string ClientName,
    DateTime AppointmentDate,
    string? ArtistName,
    Guid? ArtistId,
    string? ConsentTextSnapshot);
