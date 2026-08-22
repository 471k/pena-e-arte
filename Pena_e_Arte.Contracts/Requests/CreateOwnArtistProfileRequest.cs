namespace Pena_e_Arte.Contracts.Requests;

public record CreateOwnArtistProfileRequest(
    string FirstName,
    string LastName,
    string? Specializations,
    decimal? HourlyRate = null);
