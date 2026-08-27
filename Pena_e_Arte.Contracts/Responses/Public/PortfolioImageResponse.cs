namespace Pena_e_Arte.Contracts.Responses.Public;

public record PortfolioImageResponse(
    Guid ImageId,
    string ImageUrl,
    string? Style,              // nullable — untagged images are valid
    string? Category,           // nullable — uncategorized images are valid; fresh/healed/design
    string ArtistName,
    string ArtistSlug,
    string StudioName,
    string StudioSlug,
    double? AverageRating,      // artist-level rating; null = no reviews yet
    int ReviewCount,        // artist-level
    double? ImageAverageRating, // rating for this specific image; null = no reviews
    int ImageReviewCount,   // review count for this specific image
    double? DistanceKm,         // null when no location context provided
    long ViewCount);         // from Redis; 0 when not yet viewed
