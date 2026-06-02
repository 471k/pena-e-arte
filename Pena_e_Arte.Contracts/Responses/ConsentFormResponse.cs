namespace Pena_e_Arte.Contracts.Responses;

public record ConsentFormResponse(
    Guid      Id,
    Guid      StudioId,
    Guid      ClientId,
    Guid      AppointmentId,
    string?   FileUrl,
    string?   SignatureData,
    DateTime? SignedAt,
    DateTime  CreatedAt);
