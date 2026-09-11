namespace Pena_e_Arte.Contracts.Responses.ExternalApi;

/// <summary>
/// Deliberately a separate, trimmed-down shape from the internal AppointmentResponse —
/// a third-party integration's contract should evolve independently of the app's own UI
/// needs, and should never see internal-only fields (tattoo description, safety notes, etc.).
/// </summary>
public record ExternalAppointmentResponse(
    Guid Id,
    DateTime Date,
    int DurationMinutes,
    string Status,
    string? ClientName,
    string? ArtistName,
    decimal DepositAmount,
    string DepositStatus,
    DateTime CreatedAt);
