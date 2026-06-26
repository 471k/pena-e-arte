namespace Pena_e_Arte.Contracts.Requests;

public record UpdateArtistRequest(
    string   FirstName,
    string   LastName,
    string   Email,
    string?  Specializations,
    decimal? HourlyRate = null,
    string?  Slug       = null);
