namespace Pena_e_Arte.Contracts.Requests;

public record CreateArtistRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Specializations,
    decimal? HourlyRate = null);
