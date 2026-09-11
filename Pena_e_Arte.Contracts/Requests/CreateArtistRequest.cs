namespace Pena_e_Arte.Contracts.Requests;

public record CreateArtistRequest(
    string FirstName,
    string LastName,
    string Email,
    List<string>? Specializations,
    decimal? HourlyRate = null);
