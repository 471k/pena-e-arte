namespace Pena_e_Arte.Contracts.Responses;

public record IntakeFormResponse(
    Guid Id,
    Guid StudioId,
    Guid ClientId,
    Guid? AppointmentId,
    string FormData,
    string? FileUrl,
    DateTime? SubmittedAt,
    DateTime CreatedAt);
