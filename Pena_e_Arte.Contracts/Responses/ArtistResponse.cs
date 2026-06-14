namespace Pena_e_Arte.Contracts.Responses;

public record ArtistResponse(
    Guid     Id,
    Guid     StudioId,
    string   FirstName,
    string   LastName,
    string   Email,
    string?  Specializations,
    decimal? HourlyRate,
    DateTime CreatedAt,
    DateTime UpdatedAt);
