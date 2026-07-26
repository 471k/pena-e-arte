namespace Pena_e_Arte.Contracts.Requests;

public record UpsertClientProfileRequest(
    DateOnly? DateOfBirth,
    string? MedicalNotes,
    string? Allergies);
