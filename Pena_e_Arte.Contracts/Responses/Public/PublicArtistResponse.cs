namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistResponse(
    Guid ArtistId,
    string Name,
    string Slug,
    string? Bio,
    string? ProfileImageUrl,
    IReadOnlyList<ArtistPortfolioImageResponse> PortfolioImages,
    string? Specializations,
    decimal? HourlyRate,
    double? AverageRating,
    int ReviewCount,
    string StudioName,
    string StudioSlug,
    bool ShowBookingCta,
    bool IsOwnProfile);
