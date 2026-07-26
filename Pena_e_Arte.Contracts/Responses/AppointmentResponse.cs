namespace Pena_e_Arte.Contracts.Responses;

public record AppointmentResponse(
    Guid Id,
    Guid StudioId,
    Guid ArtistId,
    Guid ClientId,
    DateTime Date,
    DateTime EndDate,
    int DurationMinutes,
    string Status,
    string DepositStatus,
    decimal DepositAmount,
    string? Notes,
    DateTime CreatedAt,
    string? CancellationReason = null,
    DateTime? AftercareSentAt = null,
    string? ClientName = null,
    IReadOnlyList<string>? ImageUrls = null);
