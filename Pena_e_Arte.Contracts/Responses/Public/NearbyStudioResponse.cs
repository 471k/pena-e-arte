namespace Pena_e_Arte.Contracts.Responses.Public;

public record NearbyStudioResponse(
    Guid    StudioId,
    string  Name,
    string  Slug,
    string  City,
    string? CoverImageUrl,
    double  DistanceKm,
    int     ArtistCount);
