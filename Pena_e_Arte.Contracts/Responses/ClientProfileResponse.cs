namespace Pena_e_Arte.Contracts.Responses;

public record ClientProfileResponse(
    Guid Id,
    Guid ClientId,
    Guid StudioId,
    DateOnly? DateOfBirth,
    string? MedicalNotes,
    string? Allergies,
    List<string> BodyMapLocations,
    DateTime UpdatedAt,
    bool AllowCrossTenantRead);
