namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicBookingArtistResponse(
    Guid ArtistId,
    string Name,
    string? AvatarUrl,
    string? Specializations,
    decimal? HourlyRate);   // client-side deposit-percent preview — same number already reachable via
                            // AppointmentResponse.DepositAmount for authenticated bookings today
