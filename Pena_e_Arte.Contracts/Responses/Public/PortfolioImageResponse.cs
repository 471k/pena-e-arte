namespace Pena_e_Arte.Contracts.Responses.Public;

public record PortfolioImageResponse(
    string  ImageUrl,
    string  ArtistName,
    string  ArtistSlug,
    string  StudioName,
    string  StudioSlug,
    double? AverageRating,   // null = no artist reviews yet
    int     ReviewCount,
    double? DistanceKm,      // null when no location context provided
    long    ViewCount);      // from Redis; 0 when not yet viewed
