namespace Pena_e_Arte.Contracts.Requests;

public record CreateOwnArtistProfileRequest(
    string FirstName,
    string LastName,
    List<string>? Specializations,
    decimal? HourlyRate = null);
